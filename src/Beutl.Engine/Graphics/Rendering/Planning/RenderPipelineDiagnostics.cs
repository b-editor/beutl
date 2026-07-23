using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace Beutl.Graphics.Rendering;

public enum RenderIntent
{
    Preview,
    Delivery,
}

public enum RenderRequestPurpose
{
    Frame,
    HitTest,
    Bounds,
    CacheWarmup,
    Auxiliary,
}

internal interface IRenderPipelineDiagnosticsState
{
    RenderPipelineDiagnosticSnapshot Latest { get; }

    RenderPipelineDiagnosticSnapshot LatestFrame { get; }

    event Action<RenderPipelineDiagnosticSnapshot>? RequestCompleted;

    void Reset();

    void Complete(RenderPipelineDiagnosticSnapshot snapshot);
}

internal sealed class RenderPipelineDiagnosticsState : IRenderPipelineDiagnosticsState
{
    private readonly object _gate = new();
    private PublishedState _published = PublishedState.Empty;
    private Action<RenderPipelineDiagnosticSnapshot>? _requestCompleted;

    public RenderPipelineDiagnosticSnapshot Latest => Volatile.Read(ref _published).Latest;

    public RenderPipelineDiagnosticSnapshot LatestFrame => Volatile.Read(ref _published).LatestFrame;

    public event Action<RenderPipelineDiagnosticSnapshot>? RequestCompleted
    {
        add
        {
            lock (_gate)
            {
                _requestCompleted += value;
            }
        }
        remove
        {
            lock (_gate)
            {
                _requestCompleted -= value;
            }
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            Volatile.Write(ref _published, PublishedState.Empty);
        }
    }

    public void Complete(RenderPipelineDiagnosticSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (ReferenceEquals(snapshot, RenderPipelineDiagnosticSnapshot.Empty))
        {
            throw new ArgumentException("The empty diagnostic snapshot does not represent a completed request.", nameof(snapshot));
        }

        Action<RenderPipelineDiagnosticSnapshot>? observers;
        lock (_gate)
        {
            PublishedState current = _published;
            RenderPipelineDiagnosticSnapshot latestFrame = snapshot.Purpose == RenderRequestPurpose.Frame
                ? snapshot
                : current.LatestFrame;
            Volatile.Write(ref _published, new PublishedState(snapshot, latestFrame));
            observers = _requestCompleted;
        }

        NotifyObservers(observers, snapshot);
    }

    private static void NotifyObservers(
        Action<RenderPipelineDiagnosticSnapshot>? observers,
        RenderPipelineDiagnosticSnapshot snapshot)
    {
        if (observers is null)
            return;

        foreach (Delegate subscriber in observers.GetInvocationList())
        {
            try
            {
                ((Action<RenderPipelineDiagnosticSnapshot>)subscriber)(snapshot);
            }
            catch (Exception)
            {
                // Diagnostics observers cannot participate in, replace, or mask the render outcome.
            }
        }
    }

    private sealed class PublishedState(
        RenderPipelineDiagnosticSnapshot latest,
        RenderPipelineDiagnosticSnapshot latestFrame)
    {
        public static PublishedState Empty { get; } = new(
            RenderPipelineDiagnosticSnapshot.Empty,
            RenderPipelineDiagnosticSnapshot.Empty);

        public RenderPipelineDiagnosticSnapshot Latest { get; } = latest;

        public RenderPipelineDiagnosticSnapshot LatestFrame { get; } = latestFrame;
    }
}

internal static class RenderRequestDiagnostics
{
    private static readonly ConditionalWeakTable<RenderRequest, RenderPipelineDiagnosticRecorder> s_recorders = new();

    public static RenderPipelineDiagnosticRecorder? Start(RenderRequest request, string rootTargetClass)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootTargetClass);
        if (s_recorders.TryGetValue(request, out RenderPipelineDiagnosticRecorder? existing))
            return existing;

        RenderPipelineDiagnosticRecorder? recorder = RenderPipelineDiagnosticRecorder.Start(request, rootTargetClass);
        if (recorder is null)
            return null;

        try
        {
            s_recorders.Add(request, recorder);
            return recorder;
        }
        catch (ArgumentException)
        {
            return s_recorders.TryGetValue(request, out existing) ? existing : null;
        }
    }

    public static RenderPipelineDiagnosticRecorder? TryGet(RenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return s_recorders.TryGetValue(request, out RenderPipelineDiagnosticRecorder? recorder)
            ? recorder
            : null;
    }

    public static void Complete(RenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!s_recorders.TryGetValue(request, out RenderPipelineDiagnosticRecorder? recorder))
            return;

        recorder.Complete();
        s_recorders.Remove(request);
    }
}

internal sealed class RenderPipelineDiagnosticRecorder
{
    private static long s_nextRequestId;

    private readonly IRenderPipelineDiagnosticsState _state;
    private readonly RenderIntent _intent;
    private readonly RenderRequestPurpose _purpose;
    private readonly string _rootTargetClass;
    private readonly long? _parentRequestId;
    private readonly bool _requestScoped;
    private readonly Dictionary<RenderPipelineCounter, long> _counters = [];
    private readonly List<RenderPipelineDiagnosticEvent> _events = [];
    private readonly Dictionary<long, FragmentState> _fragments = [];
    private readonly Dictionary<long, PlannedWork> _plannedWorkByFragment = [];
    private readonly HashSet<long> _executedPasses = [];
    private readonly HashSet<long> _executedBackendTransitions = [];
    private readonly List<long> _pendingCacheCaptures = [];
    private readonly long _requestId;
    private long _nextSubjectId;
    private long _liveIntermediates;
    private RenderPipelineFailurePhase? _failurePhase;
    private bool _hasOpaqueExternalWork;
    private bool _cacheCapturesAccepted;
    private bool _faulted;
    private bool _completed;

    private RenderPipelineDiagnosticRecorder(
        IRenderPipelineDiagnosticsState state,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        string rootTargetClass,
        long? requestId = null,
        long? parentRequestId = null,
        bool requestScoped = false)
    {
        _state = state;
        _intent = intent;
        _purpose = purpose;
        _rootTargetClass = rootTargetClass;
        _parentRequestId = parentRequestId;
        _requestScoped = requestScoped;

        long diagnosticRequestId = requestId ?? Interlocked.Increment(ref s_nextRequestId);
        _requestId = diagnosticRequestId > 0 ? diagnosticRequestId : 1;
        AddEvent(RenderPipelineDiagnosticEventKind.RequestStarted);
    }

    internal static RenderPipelineDiagnosticRecorder? Start(
        IRenderPipelineDiagnosticsState? state,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        string rootTargetClass)
    {
        if (state is null)
            return null;

        try
        {
            return new RenderPipelineDiagnosticRecorder(state, intent, purpose, rootTargetClass);
        }
        catch (Exception)
        {
            // Observation is best-effort and cannot participate in the render result.
            return null;
        }
    }

    internal static RenderPipelineDiagnosticRecorder? Start(
        RenderRequest request,
        string rootTargetClass)
    {
        ArgumentNullException.ThrowIfNull(request);
        IRenderPipelineDiagnosticsState? state = request.Options.Diagnostics;
        if (state is null)
            return null;

        try
        {
            return new RenderPipelineDiagnosticRecorder(
                state,
                request.Options.Intent,
                request.Options.Purpose,
                rootTargetClass,
                request.Id.Value,
                request.ParentId?.Value,
                requestScoped: true);
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal void RecordGraph(RecordedRenderGraph graph)
    {
        if (_completed || _faulted)
            return;

        try
        {
            if (graph.RequestId.Value != _requestId)
                throw new InvalidOperationException("The recorded graph belongs to a different diagnostic request.");

            foreach (RecordedRenderFragment recorded in graph.Fragments)
            {
                if (recorded.Payload is not RenderFragmentReference reference)
                    throw new InvalidOperationException("A recorded fragment is missing its semantic reference.");
                RecordCommittedReference(reference);
            }
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    internal void RecordCommittedFragments(IEnumerable<RecordedRenderFragmentEntry> fragments)
    {
        if (_completed || _faulted)
            return;

        ArgumentNullException.ThrowIfNull(fragments);
        try
        {
            foreach (RecordedRenderFragmentEntry fragment in fragments)
                RecordCommittedReference(fragment.Reference);
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    private void RecordCommittedReference(RenderFragmentReference reference)
    {
        RenderFragmentId id = reference.Id
            ?? throw new InvalidOperationException("A diagnostic fragment must be committed before it is recorded.");
        if (id.RequestId.Value != _requestId)
            throw new InvalidOperationException("A diagnostic fragment belongs to a different request.");
        if (_fragments.ContainsKey(id.Value))
            return;

        _fragments.Add(id.Value, new FragmentState(RenderPipelineOutcome.Executed, false));
        Increment(RenderPipelineCounter.RecordedFragments);
        AddEvent(RenderPipelineDiagnosticEventKind.FragmentRecorded, id.Value);
        Add(RenderPipelineCounter.RecordedMaterializableValues, reference.ValueIds.Length);
        switch (reference.Kind)
        {
            case RenderFragmentKind.TargetCommand:
            case RenderFragmentKind.RawTargetCommand:
                Increment(RenderPipelineCounter.RecordedTargetCommands);
                break;
            case RenderFragmentKind.TargetCapture:
            case RenderFragmentKind.BuiltInBackdropCapture:
                Increment(RenderPipelineCounter.RecordedTargetCaptures);
                break;
            case RenderFragmentKind.TargetScope:
            case RenderFragmentKind.RawTargetScope:
                Increment(RenderPipelineCounter.RecordedTargetScopes);
                break;
            case RenderFragmentKind.Layer:
            case RenderFragmentKind.TargetLayerScope:
                Increment(RenderPipelineCounter.RecordedLayers);
                break;
        }
    }

    internal void RecordNestedRequest(RenderRequestId requestId)
    {
        if (_completed || _faulted)
            return;

        try
        {
            AddEvent(
                RenderPipelineDiagnosticEventKind.NestedRequest,
                relatedRequestId: requestId.Value);
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    internal void RecordPlan(ExecutionIslandPlan plan)
    {
        if (_completed || _faulted)
            return;

        ArgumentNullException.ThrowIfNull(plan);
        try
        {
            foreach (ExecutionIsland island in plan.Islands)
            {
                Increment(RenderPipelineCounter.ExecutionIslands);
                long subjectId = island.Fragments[^1].Value;
                bool hasGpuPass = island.PlansGpuPass;
                bool requiresSynchronization = island.Kind == ExecutionIslandKind.Readback;
                bool requiresBackendTransition = false;
                var work = new PlannedWork(
                    subjectId,
                    island.Fragments.Select(static item => item.Value).ToArray(),
                    hasGpuPass,
                    requiresSynchronization,
                    requiresBackendTransition,
                    island.ShaderRun?.Stages.Length ?? 0,
                    island.ShaderRun?.IsFused == true);
                foreach (RenderFragmentId fragmentId in island.Fragments)
                    _plannedWorkByFragment[fragmentId.Value] = work;

                if (hasGpuPass)
                {
                    Increment(RenderPipelineCounter.PlannedGpuPasses);
                    AddEvent(RenderPipelineDiagnosticEventKind.PassPlanned, subjectId);
                }
                if (requiresSynchronization)
                {
                    AddEvent(RenderPipelineDiagnosticEventKind.SynchronizationPlanned, subjectId);
                    work.SynchronizationPlanned = true;
                }
            }

            foreach (ExecutionIslandBoundary boundary in plan.Boundaries)
            {
                long subjectId = boundary.AfterFragmentId?.Value
                    ?? boundary.BeforeFragmentId?.Value
                    ?? 0;
                RenderPipelineBoundaryReason reason = MapBoundaryReason(boundary.Reason);
                Increment(RenderPipelineCounter.OpaqueBoundaries);
                AddEvent(
                    RenderPipelineDiagnosticEventKind.BoundaryPlanned,
                    subjectId,
                    boundaryReason: reason);

                if (reason is RenderPipelineBoundaryReason.LegacyCustomEffect
                    or RenderPipelineBoundaryReason.LegacyRawCanvas)
                {
                    _hasOpaqueExternalWork = true;
                    if (_fragments.TryGetValue(subjectId, out FragmentState? fragment))
                        fragment.IsOpaqueExternal = true;
                }

                if (reason == RenderPipelineBoundaryReason.ThreeD)
                    Increment(RenderPipelineCounter.Opaque3DBoundaries);

                bool synchronization = reason is RenderPipelineBoundaryReason.Readback
                    or RenderPipelineBoundaryReason.BackendTransition
                    or RenderPipelineBoundaryReason.ThreeD;
                bool transition = reason is RenderPipelineBoundaryReason.BackendTransition
                    or RenderPipelineBoundaryReason.ThreeD;
                if (!_plannedWorkByFragment.TryGetValue(subjectId, out PlannedWork? work))
                {
                    work = new PlannedWork(subjectId, [subjectId], false, synchronization, transition, 0, false);
                    _plannedWorkByFragment[subjectId] = work;
                }
                else
                {
                    work.RequiresSynchronization |= synchronization;
                    work.RequiresBackendTransition |= transition;
                }

                if (synchronization && !work.SynchronizationPlanned)
                {
                    work.SynchronizationPlanned = true;
                    AddEvent(RenderPipelineDiagnosticEventKind.SynchronizationPlanned, subjectId);
                }
                if (transition && !work.BackendTransitionPlanned)
                {
                    work.BackendTransitionPlanned = true;
                    Increment(RenderPipelineCounter.PlannedBackendTransitions);
                    AddEvent(RenderPipelineDiagnosticEventKind.BackendTransitionPlanned, subjectId);
                }
            }
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    internal long[] RecordFragments(int count, RenderPipelineOutcome intendedOutcome)
    {
        if (_completed || _faulted || count <= 0)
            return [];

        try
        {
            var result = new long[count];
            for (int i = 0; i < result.Length; i++)
            {
                long subjectId = ++_nextSubjectId;
                result[i] = subjectId;
                bool isOpaqueExternal = intendedOutcome == RenderPipelineOutcome.Executed;
                _fragments.Add(subjectId, new FragmentState(intendedOutcome, isOpaqueExternal));
                Increment(RenderPipelineCounter.RecordedFragments);
                AddEvent(RenderPipelineDiagnosticEventKind.FragmentRecorded, subjectId);

                if (isOpaqueExternal)
                {
                    _hasOpaqueExternalWork = true;
                    Increment(RenderPipelineCounter.OpaqueBoundaries);
                    AddEvent(
                        RenderPipelineDiagnosticEventKind.BoundaryPlanned,
                        subjectId,
                        boundaryReason: RenderPipelineBoundaryReason.LegacyRawCanvas);
                }
            }

            return result;
        }
        catch (Exception)
        {
            _faulted = true;
            return [];
        }
    }

    internal void RecordCacheDecision(bool cacheHit)
        => RecordCacheDecision(subjectId: 0, cacheHit);

    internal void RecordCacheDecision(long subjectId, bool cacheHit)
    {
        if (_completed || _faulted)
            return;

        try
        {
            Increment(cacheHit
                ? RenderPipelineCounter.RenderCacheHits
                : RenderPipelineCounter.RenderCacheMisses);
            AddEvent(RenderPipelineDiagnosticEventKind.CacheDecision, subjectId);
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    internal void RecordProgramCacheDecision(long subjectId, bool cacheHit)
    {
        if (_completed || _faulted)
            return;

        try
        {
            if (cacheHit)
            {
                Increment(RenderPipelineCounter.ProgramHits);
            }
            else
            {
                Increment(RenderPipelineCounter.ProgramMisses);
                Increment(RenderPipelineCounter.ProgramCreations);
            }
            AddEvent(RenderPipelineDiagnosticEventKind.CacheDecision, subjectId);
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    internal void RecordStructuralPlanDecision(bool cacheHit, bool compiled)
    {
        if (_completed || _faulted)
            return;

        try
        {
            Increment(cacheHit
                ? RenderPipelineCounter.StructuralPlanHits
                : RenderPipelineCounter.StructuralPlanMisses);
            if (compiled)
                Increment(RenderPipelineCounter.StructuralPlanCompilations);
            AddEvent(RenderPipelineDiagnosticEventKind.CacheDecision);
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    internal void RecordAllOutcomes(RenderPipelineOutcome outcome)
    {
        if (_completed || _faulted)
            return;

        try
        {
            foreach (long subjectId in _fragments.Keys)
                RecordOutcomeCore(subjectId, outcome);
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    internal void RecordFragmentExecuted(long subjectId)
    {
        if (_completed || _faulted)
            return;

        try
        {
            if (_plannedWorkByFragment.TryGetValue(subjectId, out PlannedWork? work))
            {
                foreach (long fragmentId in work.FragmentIds)
                    RecordOutcomeCore(fragmentId, RenderPipelineOutcome.Executed);
            }
            else
            {
                RecordOutcomeCore(subjectId, RenderPipelineOutcome.Executed);
            }
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    internal void RecordGpuPassExecuted(long subjectId)
    {
        if (_completed || _faulted)
            return;

        try
        {
            if (!_plannedWorkByFragment.TryGetValue(subjectId, out PlannedWork? work)
                || !work.HasGpuPass)
            {
                throw new InvalidOperationException(
                    "An executed GPU pass must belong to planned GPU-pass work.");
            }

            if (_executedPasses.Add(work.SubjectId))
            {
                Increment(RenderPipelineCounter.ExecutedGpuPasses);
                AddEvent(RenderPipelineDiagnosticEventKind.PassExecuted, work.SubjectId);
                if (work.IsFused)
                    Add(RenderPipelineCounter.FusedStages, work.ShaderStageCount);
            }
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    internal void RecordSynchronizationExecuted(long subjectId)
    {
        if (_completed || _faulted)
            return;

        try
        {
            if (!_plannedWorkByFragment.TryGetValue(subjectId, out PlannedWork? work)
                || !work.RequiresSynchronization)
            {
                throw new InvalidOperationException(
                    "An executed synchronization must belong to planned synchronization work.");
            }

            Increment(RenderPipelineCounter.Synchronizations);
            AddEvent(RenderPipelineDiagnosticEventKind.SynchronizationExecuted, work.SubjectId);
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    internal void RecordBackendTransitionExecuted(long subjectId)
    {
        if (_completed || _faulted)
            return;

        try
        {
            if (!_plannedWorkByFragment.TryGetValue(subjectId, out PlannedWork? work)
                || !work.RequiresBackendTransition)
            {
                throw new InvalidOperationException(
                    "An executed backend transition must belong to planned transition work.");
            }
            if (_executedBackendTransitions.Add(work.SubjectId))
            {
                Increment(RenderPipelineCounter.ExecutedBackendTransitions);
                AddEvent(RenderPipelineDiagnosticEventKind.BackendTransitionExecuted, work.SubjectId);
            }
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    internal void RecordOpaqueExecution(long subjectId)
    {
        if (_completed || _faulted)
            return;

        try
        {
            if (_fragments.TryGetValue(subjectId, out FragmentState? fragment)
                && fragment.IsOpaqueExternal)
            {
                Increment(RenderPipelineCounter.OpaqueExternalExecutions);
            }
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    internal void RecordIntermediateCreated()
        => RecordIntermediateAcquired(created: true, poolHit: false);

    internal void RecordIntermediateAcquired(bool created, bool poolHit)
    {
        if (_completed || _faulted)
            return;

        try
        {
            Increment(RenderPipelineCounter.IntermediateAcquires);
            if (created)
                Increment(RenderPipelineCounter.IntermediateCreates);
            if (poolHit)
                Increment(RenderPipelineCounter.PoolHits);
            else
                Increment(RenderPipelineCounter.PoolMisses);
            _liveIntermediates++;
            SetMaximum(RenderPipelineCounter.PeakLiveIntermediates, _liveIntermediates);
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    internal void RecordPoolMissWithoutAcquisition(long count = 1)
    {
        if (_completed || _faulted || count <= 0)
            return;

        try
        {
            Add(RenderPipelineCounter.PoolMisses, count);
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    internal void RecordIntermediateDischarged()
    {
        if (_completed || _faulted || _liveIntermediates <= 0)
            return;

        try
        {
            _liveIntermediates--;
            Increment(RenderPipelineCounter.IntermediateDischarges);
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    internal void RecordExternalRootResource()
    {
        if (_completed || _faulted)
            return;

        try
        {
            Increment(RenderPipelineCounter.ExternalRootResources);
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    internal void RecordMaterialization(bool fullFrame)
    {
        if (_completed || _faulted)
            return;

        try
        {
            Increment(fullFrame
                ? RenderPipelineCounter.FullFrameMaterializations
                : RenderPipelineCounter.RoiMaterializations);
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    internal void RecordCacheCaptureStaged(long subjectId)
    {
        if (_completed || _faulted)
            return;

        try
        {
            _pendingCacheCaptures.Add(subjectId);
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    internal void RecordCacheCaptureRejected()
    {
        if (_completed || _faulted)
            return;

        try
        {
            Increment(RenderPipelineCounter.RejectedRenderCacheCaptures);
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    internal void CommitAcceptedCacheCaptures()
    {
        if (_completed || _faulted)
            return;

        try
        {
            _cacheCapturesAccepted = true;
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    internal void RecordOutcome(long subjectId, RenderPipelineOutcome outcome)
    {
        if (_completed || _faulted)
            return;

        try
        {
            RecordOutcomeCore(subjectId, outcome);
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    internal void RecordFailure(RenderPipelineFailurePhase phase, long? subjectId = null)
    {
        if (_completed || _faulted || _failurePhase.HasValue)
            return;

        try
        {
            _failurePhase = phase;
            Increment(RenderPipelineCounter.Failures);
            AddEvent(RenderPipelineDiagnosticEventKind.Failure, subjectId ?? 0, failurePhase: phase);
            if (subjectId.HasValue)
            {
                RecordOutcomeCore(subjectId.Value, RenderPipelineOutcome.Failed);
            }
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    internal void RecordCleanupFailure(long? subjectId = null)
    {
        if (_completed || _faulted)
            return;

        try
        {
            if (!_failurePhase.HasValue)
            {
                _failurePhase = RenderPipelineFailurePhase.Cleanup;
                Increment(RenderPipelineCounter.Failures);
                AddEvent(
                    RenderPipelineDiagnosticEventKind.Failure,
                    subjectId ?? 0,
                    failurePhase: RenderPipelineFailurePhase.Cleanup);
                if (subjectId.HasValue)
                {
                    RecordOutcomeCore(subjectId.Value, RenderPipelineOutcome.Failed);
                }
            }

            Increment(RenderPipelineCounter.CleanupFailures);
            AddEvent(
                RenderPipelineDiagnosticEventKind.CleanupFailure,
                subjectId ?? 0,
                failurePhase: RenderPipelineFailurePhase.Cleanup);
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    internal void RecordFamilyFailure(RenderPipelineFailurePhase phase)
    {
        if (phase == RenderPipelineFailurePhase.Cleanup)
        {
            if (!_counters.TryGetValue(RenderPipelineCounter.CleanupFailures, out long count) || count == 0)
                RecordCleanupFailure();
        }
        else
        {
            RecordFailure(phase);
        }
    }

    internal void Complete()
    {
        if (_completed)
            return;

        _completed = true;
        if (_faulted)
            return;

        try
        {
            foreach ((long subjectId, FragmentState fragment) in _fragments)
            {
                if (!fragment.Outcome.HasValue)
                {
                    RenderPipelineOutcome outcome = _failurePhase.HasValue
                        ? RenderPipelineOutcome.Skipped
                        : _purpose is RenderRequestPurpose.Bounds or RenderRequestPurpose.HitTest
                            ? RenderPipelineOutcome.Metadata
                            : _requestScoped
                                ? RenderPipelineOutcome.Skipped
                                : fragment.IntendedOutcome;
                    fragment.Outcome = outcome;
                    Increment(GetOutcomeCounter(outcome));
                    AddEvent(RenderPipelineDiagnosticEventKind.OutcomeAssigned, subjectId, outcome: outcome);
                }
            }

            if (_cacheCapturesAccepted)
            {
                CommitAcceptedCacheCapturesCore();
            }
            else
            {
                Add(RenderPipelineCounter.RejectedRenderCacheCaptures, _pendingCacheCaptures.Count);
            }
            _pendingCacheCaptures.Clear();

            AddEvent(RenderPipelineDiagnosticEventKind.RequestCompleted);

            RenderPipelineDiagnosticSnapshot snapshot = RenderPipelineDiagnosticSnapshot.Create(
                _requestId,
                _parentRequestId,
                _intent,
                _purpose,
                succeeded: !_failurePhase.HasValue,
                _hasOpaqueExternalWork,
                _rootTargetClass,
                _failurePhase,
                _counters,
                _events);
            _state.Complete(snapshot);
        }
        catch (Exception)
        {
            // Diagnostics must not participate in, replace, or mask the render outcome.
        }
    }

    private void CommitAcceptedCacheCapturesCore()
    {
        foreach (long subjectId in _pendingCacheCaptures)
        {
            Increment(RenderPipelineCounter.RenderCacheCaptures);
            AddEvent(RenderPipelineDiagnosticEventKind.CacheCapturePublished, subjectId);
        }

        _pendingCacheCaptures.Clear();
    }

    private void RecordOutcomeCore(long subjectId, RenderPipelineOutcome outcome)
    {
        if (!_fragments.TryGetValue(subjectId, out FragmentState? fragment)
            || fragment.Outcome.HasValue)
        {
            return;
        }

        fragment.Outcome = outcome;
        Increment(GetOutcomeCounter(outcome));
        AddEvent(RenderPipelineDiagnosticEventKind.OutcomeAssigned, subjectId, outcome: outcome);
    }

    private void Increment(RenderPipelineCounter counter)
    {
        Add(counter, 1);
    }

    private void Add(RenderPipelineCounter counter, long amount)
    {
        if (amount == 0)
            return;
        _counters.TryGetValue(counter, out long value);
        _counters[counter] = checked(value + amount);
    }

    private void SetMaximum(RenderPipelineCounter counter, long value)
    {
        _counters.TryGetValue(counter, out long current);
        if (value > current)
        {
            _counters[counter] = value;
        }
    }

    private void AddEvent(
        RenderPipelineDiagnosticEventKind kind,
        long subjectId = 0,
        long? relatedRequestId = null,
        RenderPipelineBoundaryReason? boundaryReason = null,
        RenderPipelineOutcome? outcome = null,
        RenderPipelineFailurePhase? failurePhase = null)
    {
        _events.Add(new RenderPipelineDiagnosticEvent(
            _events.Count,
            kind,
            subjectId,
            relatedRequestId,
            boundaryReason,
            outcome,
            failurePhase));
    }

    private static RenderPipelineCounter GetOutcomeCounter(RenderPipelineOutcome outcome)
    {
        return outcome switch
        {
            RenderPipelineOutcome.Executed => RenderPipelineCounter.ExecutedOutcomes,
            RenderPipelineOutcome.Cached => RenderPipelineCounter.CachedOutcomes,
            RenderPipelineOutcome.Metadata => RenderPipelineCounter.MetadataOutcomes,
            RenderPipelineOutcome.Skipped => RenderPipelineCounter.SkippedOutcomes,
            RenderPipelineOutcome.Failed => RenderPipelineCounter.FailedOutcomes,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "The outcome is not defined."),
        };
    }

    private static RenderPipelineBoundaryReason MapBoundaryReason(ExecutionIslandBoundaryReason reason)
    {
        return reason switch
        {
            ExecutionIslandBoundaryReason.MaterializedInput => RenderPipelineBoundaryReason.CacheInput,
            ExecutionIslandBoundaryReason.CoverageResolution => RenderPipelineBoundaryReason.UnsafeComposite,
            ExecutionIslandBoundaryReason.WholeSourceShader => RenderPipelineBoundaryReason.UnsafeComposite,
            ExecutionIslandBoundaryReason.Geometry => RenderPipelineBoundaryReason.Geometry,
            ExecutionIslandBoundaryReason.Opaque => RenderPipelineBoundaryReason.Opaque,
            ExecutionIslandBoundaryReason.LegacyCustomEffect => RenderPipelineBoundaryReason.LegacyCustomEffect,
            ExecutionIslandBoundaryReason.TargetCommand => RenderPipelineBoundaryReason.TargetCommand,
            ExecutionIslandBoundaryReason.TargetCapture => RenderPipelineBoundaryReason.TargetCapture,
            ExecutionIslandBoundaryReason.TargetScope => RenderPipelineBoundaryReason.TargetScope,
            ExecutionIslandBoundaryReason.Layer => RenderPipelineBoundaryReason.Layer,
            ExecutionIslandBoundaryReason.Readback => RenderPipelineBoundaryReason.Readback,
            ExecutionIslandBoundaryReason.UnsafeComposite => RenderPipelineBoundaryReason.UnsafeComposite,
            ExecutionIslandBoundaryReason.SemanticComposite => RenderPipelineBoundaryReason.UnsafeComposite,
            ExecutionIslandBoundaryReason.LegacyRawCanvas => RenderPipelineBoundaryReason.LegacyRawCanvas,
            ExecutionIslandBoundaryReason.CacheInput => RenderPipelineBoundaryReason.CacheInput,
            ExecutionIslandBoundaryReason.CacheCapture => RenderPipelineBoundaryReason.CacheCapture,
            ExecutionIslandBoundaryReason.BackendTransition => RenderPipelineBoundaryReason.BackendTransition,
            ExecutionIslandBoundaryReason.ThreeD => RenderPipelineBoundaryReason.ThreeD,
            ExecutionIslandBoundaryReason.DynamicTopology => RenderPipelineBoundaryReason.DynamicTopology,
            ExecutionIslandBoundaryReason.ScaleTransition => RenderPipelineBoundaryReason.ScaleTransition,
            ExecutionIslandBoundaryReason.BackendLimit => RenderPipelineBoundaryReason.BackendLimit,
            ExecutionIslandBoundaryReason.ScopeMismatch
                or ExecutionIslandBoundaryReason.Branching
                or ExecutionIslandBoundaryReason.FusionDisabled => RenderPipelineBoundaryReason.UnsafeComposite,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "The boundary reason is not defined."),
        };
    }

    private sealed class PlannedWork(
        long subjectId,
        IReadOnlyList<long> fragmentIds,
        bool hasGpuPass,
        bool requiresSynchronization,
        bool requiresBackendTransition,
        int shaderStageCount,
        bool isFused)
    {
        public long SubjectId { get; } = subjectId;

        public IReadOnlyList<long> FragmentIds { get; } = fragmentIds;

        public bool HasGpuPass { get; } = hasGpuPass;

        public bool RequiresSynchronization { get; set; } = requiresSynchronization;

        public bool RequiresBackendTransition { get; set; } = requiresBackendTransition;

        public bool SynchronizationPlanned { get; set; }

        public bool BackendTransitionPlanned { get; set; }

        public int ShaderStageCount { get; } = shaderStageCount;

        public bool IsFused { get; } = isFused;
    }

    private sealed class FragmentState(
        RenderPipelineOutcome intendedOutcome,
        bool isOpaqueExternal)
    {
        public RenderPipelineOutcome IntendedOutcome { get; } = intendedOutcome;

        public bool IsOpaqueExternal { get; set; } = isOpaqueExternal;

        public RenderPipelineOutcome? Outcome { get; set; }
    }
}

internal sealed class RenderPipelineDiagnosticSnapshot
{
    private static readonly IReadOnlyDictionary<RenderPipelineCounter, long> s_emptyCounters =
        new ReadOnlyDictionary<RenderPipelineCounter, long>(new Dictionary<RenderPipelineCounter, long>());
    private static readonly IReadOnlyList<RenderPipelineDiagnosticEvent> s_emptyEvents =
        Array.AsReadOnly(Array.Empty<RenderPipelineDiagnosticEvent>());

    private RenderPipelineDiagnosticSnapshot(
        long requestId,
        long? parentRequestId,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        bool succeeded,
        bool hasOpaqueExternalWork,
        string rootTargetClass,
        RenderPipelineFailurePhase? failurePhase,
        IReadOnlyDictionary<RenderPipelineCounter, long> counters,
        IReadOnlyList<RenderPipelineDiagnosticEvent> events)
    {
        RequestId = requestId;
        ParentRequestId = parentRequestId;
        Intent = intent;
        Purpose = purpose;
        Succeeded = succeeded;
        HasOpaqueExternalWork = hasOpaqueExternalWork;
        RootTargetClass = rootTargetClass;
        FailurePhase = failurePhase;
        Counters = counters;
        Events = events;
    }

    internal static RenderPipelineDiagnosticSnapshot Empty { get; } = new(
        requestId: 0,
        parentRequestId: null,
        intent: RenderIntent.Preview,
        purpose: RenderRequestPurpose.Frame,
        succeeded: false,
        hasOpaqueExternalWork: false,
        rootTargetClass: string.Empty,
        failurePhase: null,
        counters: s_emptyCounters,
        events: s_emptyEvents);

    internal long RequestId { get; }

    internal long? ParentRequestId { get; }

    internal RenderIntent Intent { get; }

    internal RenderRequestPurpose Purpose { get; }

    internal bool Succeeded { get; }

    internal bool HasOpaqueExternalWork { get; }

    internal string RootTargetClass { get; }

    internal RenderPipelineFailurePhase? FailurePhase { get; }

    internal IReadOnlyDictionary<RenderPipelineCounter, long> Counters { get; }

    internal IReadOnlyList<RenderPipelineDiagnosticEvent> Events { get; }

    internal long this[RenderPipelineCounter counter]
        => Counters.TryGetValue(counter, out long value) ? value : 0;

    internal static RenderPipelineDiagnosticSnapshot Create(
        long requestId,
        long? parentRequestId,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        bool succeeded,
        bool hasOpaqueExternalWork,
        string rootTargetClass,
        RenderPipelineFailurePhase? failurePhase,
        IReadOnlyDictionary<RenderPipelineCounter, long> counters,
        IEnumerable<RenderPipelineDiagnosticEvent> events)
    {
        ValidateIdentity(requestId, parentRequestId, intent, purpose, rootTargetClass);
        ArgumentNullException.ThrowIfNull(counters);
        ArgumentNullException.ThrowIfNull(events);

        Dictionary<RenderPipelineCounter, long> counterCopy = CopyAndValidateCounters(counters);
        RenderPipelineDiagnosticEvent[] eventCopy = events.ToArray();
        ValidateEvents(requestId, eventCopy);
        ValidateReconciliation(purpose, succeeded, hasOpaqueExternalWork, failurePhase, counterCopy, eventCopy);

        return new RenderPipelineDiagnosticSnapshot(
            requestId,
            parentRequestId,
            intent,
            purpose,
            succeeded,
            hasOpaqueExternalWork,
            rootTargetClass,
            failurePhase,
            new ReadOnlyDictionary<RenderPipelineCounter, long>(counterCopy),
            Array.AsReadOnly(eventCopy));
    }

    private static void ValidateIdentity(
        long requestId,
        long? parentRequestId,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        string rootTargetClass)
    {
        if (requestId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestId), requestId, "Request IDs must be positive.");
        }

        if (parentRequestId is <= 0 || parentRequestId == requestId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parentRequestId),
                parentRequestId,
                "A parent request ID must be positive and different from the request ID.");
        }

        if (!Enum.IsDefined(intent))
        {
            throw new ArgumentOutOfRangeException(nameof(intent), intent, "The render intent is not defined.");
        }

        if (!Enum.IsDefined(purpose))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "The request purpose is not defined.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(rootTargetClass);
    }

    private static Dictionary<RenderPipelineCounter, long> CopyAndValidateCounters(
        IReadOnlyDictionary<RenderPipelineCounter, long> counters)
    {
        var result = new Dictionary<RenderPipelineCounter, long>(counters.Count);
        foreach ((RenderPipelineCounter counter, long value) in counters)
        {
            if (!Enum.IsDefined(counter))
            {
                throw new ArgumentException($"The counter '{counter}' is not defined.", nameof(counters));
            }

            if (value < 0)
            {
                throw new ArgumentException($"The counter '{counter}' cannot be negative.", nameof(counters));
            }

            result.Add(counter, value);
        }

        return result;
    }

    private static void ValidateEvents(long requestId, IReadOnlyList<RenderPipelineDiagnosticEvent> events)
    {
        if (events.Count < 2
            || events[0].Kind != RenderPipelineDiagnosticEventKind.RequestStarted
            || events[^1].Kind != RenderPipelineDiagnosticEventKind.RequestCompleted
            || events.Count(item => item.Kind == RenderPipelineDiagnosticEventKind.RequestStarted) != 1
            || events.Count(item => item.Kind == RenderPipelineDiagnosticEventKind.RequestCompleted) != 1)
        {
            throw new ArgumentException(
                "A diagnostic event stream must start once with RequestStarted and end once with RequestCompleted.",
                nameof(events));
        }

        var pendingPlans = new Dictionary<(RenderPipelineDiagnosticEventKind Kind, long SubjectId), int>();
        for (int i = 0; i < events.Count; i++)
        {
            RenderPipelineDiagnosticEvent item = events[i];
            if (item.Sequence != i)
            {
                throw new ArgumentException("Diagnostic event sequences must be zero-based and gap-free.", nameof(events));
            }

            if (!Enum.IsDefined(item.Kind))
            {
                throw new ArgumentException($"The event kind '{item.Kind}' is not defined.", nameof(events));
            }

            if (item.SubjectId < 0)
            {
                throw new ArgumentException("Diagnostic event subject IDs cannot be negative.", nameof(events));
            }

            ValidateOptionalFields(requestId, item);
            ValidatePlanOrdering(item, pendingPlans);
        }
    }

    private static void ValidateOptionalFields(long requestId, RenderPipelineDiagnosticEvent item)
    {
        bool expectsRelatedRequest = item.Kind == RenderPipelineDiagnosticEventKind.NestedRequest;
        bool expectsBoundaryReason = item.Kind == RenderPipelineDiagnosticEventKind.BoundaryPlanned;
        bool expectsOutcome = item.Kind == RenderPipelineDiagnosticEventKind.OutcomeAssigned;
        bool expectsFailurePhase = item.Kind is RenderPipelineDiagnosticEventKind.Failure
            or RenderPipelineDiagnosticEventKind.CleanupFailure;

        if (item.RelatedRequestId.HasValue != expectsRelatedRequest
            || item.BoundaryReason.HasValue != expectsBoundaryReason
            || item.Outcome.HasValue != expectsOutcome
            || item.FailurePhase.HasValue != expectsFailurePhase)
        {
            throw new ArgumentException(
                $"The optional fields on event '{item.Kind}' do not match its event kind.",
                "events");
        }

        if (item.RelatedRequestId is <= 0 || item.RelatedRequestId == requestId)
        {
            throw new ArgumentException(
                "A related request ID must be positive and different from the current request ID.",
                "events");
        }

        if (item.BoundaryReason is { } boundaryReason && !Enum.IsDefined(boundaryReason))
        {
            throw new ArgumentException($"The boundary reason '{boundaryReason}' is not defined.", "events");
        }

        if (item.Outcome is { } outcome && !Enum.IsDefined(outcome))
        {
            throw new ArgumentException($"The outcome '{outcome}' is not defined.", "events");
        }

        if (item.FailurePhase is { } failurePhase && !Enum.IsDefined(failurePhase))
        {
            throw new ArgumentException($"The failure phase '{failurePhase}' is not defined.", "events");
        }
    }

    private static void ValidatePlanOrdering(
        RenderPipelineDiagnosticEvent item,
        Dictionary<(RenderPipelineDiagnosticEventKind Kind, long SubjectId), int> pendingPlans)
    {
        switch (item.Kind)
        {
            case RenderPipelineDiagnosticEventKind.PassPlanned:
            case RenderPipelineDiagnosticEventKind.SynchronizationPlanned:
            case RenderPipelineDiagnosticEventKind.BackendTransitionPlanned:
                IncrementPendingPlan(pendingPlans, (item.Kind, item.SubjectId));
                break;
            case RenderPipelineDiagnosticEventKind.PassExecuted:
                ConsumePendingPlan(
                    pendingPlans,
                    (RenderPipelineDiagnosticEventKind.PassPlanned, item.SubjectId));
                break;
            case RenderPipelineDiagnosticEventKind.SynchronizationExecuted:
                RequirePendingPlan(
                    pendingPlans,
                    (RenderPipelineDiagnosticEventKind.SynchronizationPlanned, item.SubjectId));
                break;
            case RenderPipelineDiagnosticEventKind.BackendTransitionExecuted:
                ConsumePendingPlan(
                    pendingPlans,
                    (RenderPipelineDiagnosticEventKind.BackendTransitionPlanned, item.SubjectId));
                break;
        }
    }

    private static void IncrementPendingPlan(
        Dictionary<(RenderPipelineDiagnosticEventKind Kind, long SubjectId), int> pendingPlans,
        (RenderPipelineDiagnosticEventKind Kind, long SubjectId) key)
    {
        pendingPlans.TryGetValue(key, out int count);
        pendingPlans[key] = checked(count + 1);
    }

    private static void ConsumePendingPlan(
        Dictionary<(RenderPipelineDiagnosticEventKind Kind, long SubjectId), int> pendingPlans,
        (RenderPipelineDiagnosticEventKind Kind, long SubjectId) key)
    {
        if (!pendingPlans.TryGetValue(key, out int count) || count == 0)
        {
            throw new ArgumentException(
                "An executed diagnostic event must follow its corresponding planned event.",
                "events");
        }

        pendingPlans[key] = count - 1;
    }

    private static void RequirePendingPlan(
        IReadOnlyDictionary<(RenderPipelineDiagnosticEventKind Kind, long SubjectId), int> pendingPlans,
        (RenderPipelineDiagnosticEventKind Kind, long SubjectId) key)
    {
        if (!pendingPlans.TryGetValue(key, out int count) || count == 0)
        {
            throw new ArgumentException(
                "An executed diagnostic event must follow its corresponding planned event.",
                "events");
        }
    }

    private static void ValidateReconciliation(
        RenderRequestPurpose purpose,
        bool succeeded,
        bool hasOpaqueExternalWork,
        RenderPipelineFailurePhase? failurePhase,
        IReadOnlyDictionary<RenderPipelineCounter, long> counters,
        IReadOnlyList<RenderPipelineDiagnosticEvent> events)
    {
        long recordedFragments = GetCounter(counters, RenderPipelineCounter.RecordedFragments);
        long terminalOutcomes;
        try
        {
            terminalOutcomes = checked(
                GetCounter(counters, RenderPipelineCounter.ExecutedOutcomes)
                + GetCounter(counters, RenderPipelineCounter.CachedOutcomes)
                + GetCounter(counters, RenderPipelineCounter.MetadataOutcomes)
                + GetCounter(counters, RenderPipelineCounter.SkippedOutcomes)
                + GetCounter(counters, RenderPipelineCounter.FailedOutcomes));
        }
        catch (OverflowException ex)
        {
            throw new ArgumentException("Terminal outcome counters overflow their valid range.", nameof(counters), ex);
        }

        if (recordedFragments != terminalOutcomes)
        {
            throw new ArgumentException(
                "Every recorded fragment must have exactly one terminal outcome.",
                nameof(counters));
        }

        ValidateOutcomeEvidence(purpose, counters, events);
        ValidateExecutionEvidence(counters, events);

        if (GetCounter(counters, RenderPipelineCounter.IntermediateAcquires)
            != GetCounter(counters, RenderPipelineCounter.IntermediateDischarges))
        {
            throw new ArgumentException(
                "Every acquired intermediate must be discharged before request completion.",
                nameof(counters));
        }

        ValidateFailureState(succeeded, failurePhase, counters, events);
        ValidateOpaqueExternalState(hasOpaqueExternalWork, counters, events);
    }

    private static void ValidateExecutionEvidence(
        IReadOnlyDictionary<RenderPipelineCounter, long> counters,
        IReadOnlyList<RenderPipelineDiagnosticEvent> events)
    {
        ValidateEventCounter(
            RenderPipelineDiagnosticEventKind.PassPlanned,
            RenderPipelineCounter.PlannedGpuPasses,
            counters,
            events);
        ValidateEventCounter(
            RenderPipelineDiagnosticEventKind.PassExecuted,
            RenderPipelineCounter.ExecutedGpuPasses,
            counters,
            events);
        ValidateEventCounter(
            RenderPipelineDiagnosticEventKind.SynchronizationExecuted,
            RenderPipelineCounter.Synchronizations,
            counters,
            events);
        ValidateEventCounter(
            RenderPipelineDiagnosticEventKind.BackendTransitionPlanned,
            RenderPipelineCounter.PlannedBackendTransitions,
            counters,
            events);
        ValidateEventCounter(
            RenderPipelineDiagnosticEventKind.BackendTransitionExecuted,
            RenderPipelineCounter.ExecutedBackendTransitions,
            counters,
            events);

        long acquires = GetCounter(counters, RenderPipelineCounter.IntermediateAcquires);
        long creates = GetCounter(counters, RenderPipelineCounter.IntermediateCreates);
        long poolHits = GetCounter(counters, RenderPipelineCounter.PoolHits);
        long poolMisses = GetCounter(counters, RenderPipelineCounter.PoolMisses);
        long peak = GetCounter(counters, RenderPipelineCounter.PeakLiveIntermediates);
        bool hasAcquisitionClassification = creates != 0 || poolHits != 0 || poolMisses != 0;
        if (hasAcquisitionClassification
            && (checked(creates + poolHits) != acquires || poolMisses < creates))
        {
            throw new ArgumentException(
                "Intermediate create/reuse/miss counters must exactly classify successful acquisitions.",
                nameof(counters));
        }

        if (peak > acquires)
        {
            throw new ArgumentException(
                "Peak live intermediates cannot exceed successful intermediate acquisitions.",
                nameof(counters));
        }

        if (GetCounter(counters, RenderPipelineCounter.ExecutedGpuPasses)
            > GetCounter(counters, RenderPipelineCounter.PlannedGpuPasses))
        {
            throw new ArgumentException(
                "Executed GPU passes cannot exceed planned GPU passes.",
                nameof(counters));
        }

        int firstPublication = events
            .Select(static (item, index) => (item, index))
            .Where(static pair => pair.item.Kind == RenderPipelineDiagnosticEventKind.CacheCapturePublished)
            .Select(static pair => pair.index)
            .DefaultIfEmpty(int.MaxValue)
            .Min();
        if (firstPublication != int.MaxValue
            && events.Skip(firstPublication + 1).Any(static item => item.Kind is
                RenderPipelineDiagnosticEventKind.OutcomeAssigned
                or RenderPipelineDiagnosticEventKind.PassExecuted
                or RenderPipelineDiagnosticEventKind.SynchronizationExecuted
                or RenderPipelineDiagnosticEventKind.BackendTransitionExecuted
                or RenderPipelineDiagnosticEventKind.Failure
                or RenderPipelineDiagnosticEventKind.CleanupFailure))
        {
            throw new ArgumentException(
                "Cache publication must follow terminal outcomes, execution, and cleanup.",
                nameof(events));
        }
    }

    private static void ValidateEventCounter(
        RenderPipelineDiagnosticEventKind kind,
        RenderPipelineCounter counter,
        IReadOnlyDictionary<RenderPipelineCounter, long> counters,
        IReadOnlyList<RenderPipelineDiagnosticEvent> events)
    {
        if (events.LongCount(item => item.Kind == kind) != GetCounter(counters, counter))
        {
            throw new ArgumentException(
                $"Events for '{kind}' must exactly match counter '{counter}'.",
                nameof(events));
        }
    }

    private static void ValidateOutcomeEvidence(
        RenderRequestPurpose purpose,
        IReadOnlyDictionary<RenderPipelineCounter, long> counters,
        IReadOnlyList<RenderPipelineDiagnosticEvent> events)
    {
        var recordedSubjects = new HashSet<long>();
        var outcomeSubjects = new HashSet<long>();
        var outcomeCounts = new Dictionary<RenderPipelineOutcome, long>();

        foreach (RenderPipelineDiagnosticEvent item in events)
        {
            if (item.Kind == RenderPipelineDiagnosticEventKind.FragmentRecorded)
            {
                if (item.SubjectId <= 0 || !recordedSubjects.Add(item.SubjectId))
                {
                    throw new ArgumentException(
                        "Every recorded fragment event must have a unique positive subject ID.",
                        nameof(events));
                }
            }
            else if (item.Kind == RenderPipelineDiagnosticEventKind.OutcomeAssigned)
            {
                if (item.SubjectId <= 0
                    || !recordedSubjects.Contains(item.SubjectId)
                    || !outcomeSubjects.Add(item.SubjectId))
                {
                    throw new ArgumentException(
                        "Every outcome must follow and uniquely identify one recorded fragment.",
                        nameof(events));
                }

                RenderPipelineOutcome outcome = item.Outcome!.Value;
                outcomeCounts.TryGetValue(outcome, out long count);
                outcomeCounts[outcome] = checked(count + 1);
            }
        }

        if (recordedSubjects.Count != GetCounter(counters, RenderPipelineCounter.RecordedFragments)
            || outcomeSubjects.Count != recordedSubjects.Count)
        {
            throw new ArgumentException(
                "Fragment and outcome events must exactly cover the recorded fragment counter.",
                nameof(events));
        }

        ValidateOutcomeCounter(
            RenderPipelineOutcome.Executed,
            RenderPipelineCounter.ExecutedOutcomes,
            counters,
            outcomeCounts);
        ValidateOutcomeCounter(
            RenderPipelineOutcome.Cached,
            RenderPipelineCounter.CachedOutcomes,
            counters,
            outcomeCounts);
        ValidateOutcomeCounter(
            RenderPipelineOutcome.Metadata,
            RenderPipelineCounter.MetadataOutcomes,
            counters,
            outcomeCounts);
        ValidateOutcomeCounter(
            RenderPipelineOutcome.Skipped,
            RenderPipelineCounter.SkippedOutcomes,
            counters,
            outcomeCounts);
        ValidateOutcomeCounter(
            RenderPipelineOutcome.Failed,
            RenderPipelineCounter.FailedOutcomes,
            counters,
            outcomeCounts);

        long metadataOutcomes = GetCounter(counters, RenderPipelineCounter.MetadataOutcomes);
        bool isMetadataPurpose = purpose is RenderRequestPurpose.Bounds or RenderRequestPurpose.HitTest;
        if ((isMetadataPurpose && metadataOutcomes != recordedSubjects.Count)
            || (!isMetadataPurpose && metadataOutcomes != 0))
        {
            throw new ArgumentException(
                "Bounds and hit-test requests require metadata outcomes, which are invalid for other purposes.",
                nameof(counters));
        }
    }

    private static void ValidateOutcomeCounter(
        RenderPipelineOutcome outcome,
        RenderPipelineCounter counter,
        IReadOnlyDictionary<RenderPipelineCounter, long> counters,
        IReadOnlyDictionary<RenderPipelineOutcome, long> outcomeCounts)
    {
        outcomeCounts.TryGetValue(outcome, out long eventCount);
        if (eventCount != GetCounter(counters, counter))
        {
            throw new ArgumentException(
                $"Outcome events for '{outcome}' do not match counter '{counter}'.",
                nameof(counters));
        }
    }

    private static void ValidateFailureState(
        bool succeeded,
        RenderPipelineFailurePhase? failurePhase,
        IReadOnlyDictionary<RenderPipelineCounter, long> counters,
        IReadOnlyList<RenderPipelineDiagnosticEvent> events)
    {
        long failures = GetCounter(counters, RenderPipelineCounter.Failures);
        long cleanupFailures = GetCounter(counters, RenderPipelineCounter.CleanupFailures);
        long failedOutcomes = GetCounter(counters, RenderPipelineCounter.FailedOutcomes);
        long publishedCaptures = GetCounter(counters, RenderPipelineCounter.RenderCacheCaptures);
        RenderPipelineDiagnosticEvent[] primaryFailureEvents = events
            .Where(item => item.Kind == RenderPipelineDiagnosticEventKind.Failure)
            .ToArray();
        RenderPipelineDiagnosticEvent[] cleanupFailureEvents = events
            .Where(item => item.Kind == RenderPipelineDiagnosticEventKind.CleanupFailure)
            .ToArray();

        if (failures is < 0 or > 1)
        {
            throw new ArgumentException("The primary failure counter must be zero or one.", nameof(counters));
        }

        if (failurePhase is { } phase && !Enum.IsDefined(phase))
        {
            throw new ArgumentOutOfRangeException(nameof(failurePhase), failurePhase, "The failure phase is not defined.");
        }

        if (succeeded)
        {
            if (failurePhase.HasValue || failures != 0 || cleanupFailures != 0 || failedOutcomes != 0)
            {
                throw new ArgumentException("A successful request cannot contain failure state.", nameof(counters));
            }
        }
        else if (!failurePhase.HasValue || failures != 1)
        {
            throw new ArgumentException(
                "A failed request must identify one primary failure and its phase.",
                nameof(counters));
        }

        if (failurePhase == RenderPipelineFailurePhase.Cleanup && cleanupFailures == 0)
        {
            throw new ArgumentException(
                "A primary cleanup failure requires at least one cleanup failure.",
                nameof(counters));
        }

        if (cleanupFailureEvents.LongLength != cleanupFailures
            || cleanupFailureEvents.Any(item => item.FailurePhase != RenderPipelineFailurePhase.Cleanup))
        {
            throw new ArgumentException(
                "Cleanup failure events must exactly match the cleanup failure counter and phase.",
                nameof(events));
        }

        if (succeeded)
        {
            if (primaryFailureEvents.Length != 0 || cleanupFailureEvents.Length != 0)
            {
                throw new ArgumentException("A successful request cannot contain failure events.", nameof(events));
            }
        }
        else if (primaryFailureEvents.Length != 1 || primaryFailureEvents[0].FailurePhase != failurePhase)
        {
            throw new ArgumentException(
                "A failed request requires one primary failure event matching its failure phase.",
                nameof(events));
        }

        long publishedCaptureEvents = events.LongCount(
            item => item.Kind == RenderPipelineDiagnosticEventKind.CacheCapturePublished);
        if (publishedCaptureEvents != publishedCaptures)
        {
            throw new ArgumentException(
                "Published cache-capture events must exactly match the publication counter.",
                nameof(events));
        }

        bool acceptedPostCommitCleanupFailure = !succeeded
                                                && failurePhase == RenderPipelineFailurePhase.Cleanup
                                                && cleanupFailures != 0;
        if (!succeeded && !acceptedPostCommitCleanupFailure && publishedCaptures != 0)
        {
            throw new ArgumentException(
                "A failed request may publish cache captures only before a post-commit cleanup failure.",
                nameof(counters));
        }
    }

    private static void ValidateOpaqueExternalState(
        bool hasOpaqueExternalWork,
        IReadOnlyDictionary<RenderPipelineCounter, long> counters,
        IReadOnlyList<RenderPipelineDiagnosticEvent> events)
    {
        long executions = GetCounter(counters, RenderPipelineCounter.OpaqueExternalExecutions);
        long boundaries = events.LongCount(
            item => item.Kind == RenderPipelineDiagnosticEventKind.BoundaryPlanned);
        long threeDBoundaries = events.LongCount(item =>
            item.Kind == RenderPipelineDiagnosticEventKind.BoundaryPlanned
            && item.BoundaryReason == RenderPipelineBoundaryReason.ThreeD);
        long legacyBoundaries = events.LongCount(item =>
            item.Kind == RenderPipelineDiagnosticEventKind.BoundaryPlanned
            && item.BoundaryReason is RenderPipelineBoundaryReason.LegacyCustomEffect
                or RenderPipelineBoundaryReason.LegacyRawCanvas);

        if (boundaries != GetCounter(counters, RenderPipelineCounter.OpaqueBoundaries)
            || threeDBoundaries != GetCounter(counters, RenderPipelineCounter.Opaque3DBoundaries))
        {
            throw new ArgumentException(
                "Boundary events must match the aggregate and 3D boundary counters.",
                nameof(counters));
        }

        if (hasOpaqueExternalWork != (legacyBoundaries != 0) || executions > legacyBoundaries)
        {
            throw new ArgumentException(
                "Opaque external work must match its legacy boundaries, and callback entries cannot exceed them.",
                nameof(counters));
        }
    }

    private static long GetCounter(
        IReadOnlyDictionary<RenderPipelineCounter, long> counters,
        RenderPipelineCounter counter)
    {
        return counters.TryGetValue(counter, out long value) ? value : 0;
    }
}

internal readonly record struct RenderPipelineDiagnosticEvent(
    long Sequence,
    RenderPipelineDiagnosticEventKind Kind,
    long SubjectId,
    long? RelatedRequestId,
    RenderPipelineBoundaryReason? BoundaryReason,
    RenderPipelineOutcome? Outcome,
    RenderPipelineFailurePhase? FailurePhase);

internal enum RenderPipelineDiagnosticEventKind
{
    RequestStarted,
    FragmentRecorded,
    BoundaryPlanned,
    PassPlanned,
    SynchronizationPlanned,
    BackendTransitionPlanned,
    CacheDecision,
    PassExecuted,
    SynchronizationExecuted,
    BackendTransitionExecuted,
    CacheCapturePublished,
    OutcomeAssigned,
    NestedRequest,
    Failure,
    CleanupFailure,
    RequestCompleted,
}

internal enum RenderPipelineBoundaryReason
{
    Opaque,
    Geometry,
    CacheInput,
    CacheCapture,
    TargetCommand,
    TargetCapture,
    TargetScope,
    Layer,
    Readback,
    UnsafeComposite,
    LegacyRawCanvas,
    BackendTransition,
    DynamicTopology,
    ScaleTransition,
    BackendLimit,
    ThreeD,
    LegacyCustomEffect,
}

internal enum RenderPipelineOutcome
{
    Executed,
    Cached,
    Metadata,
    Skipped,
    Failed,
}

internal enum RenderPipelineFailurePhase
{
    Recording,
    Metadata,
    RegionAnalysis,
    CacheResolution,
    Planning,
    ProgramCompilation,
    Binding,
    Allocation,
    Execution,
    CachePublication,
    Cleanup,
}

internal enum RenderPipelineCounter
{
    RecordedFragments,
    RecordedMaterializableValues,
    RecordedTargetCommands,
    RecordedTargetCaptures,
    RecordedTargetScopes,
    RecordedLayers,
    PlannedGpuPasses,
    ExecutedGpuPasses,
    FusedStages,
    ExecutionIslands,
    IntermediateAcquires,
    IntermediateCreates,
    IntermediateDischarges,
    PoolHits,
    PoolMisses,
    PeakLiveIntermediates,
    FullFrameMaterializations,
    RoiMaterializations,
    Synchronizations,
    PlannedBackendTransitions,
    ExecutedBackendTransitions,
    StructuralPlanCompilations,
    StructuralPlanHits,
    StructuralPlanMisses,
    ProgramCreations,
    ProgramHits,
    ProgramMisses,
    RenderCacheHits,
    RenderCacheMisses,
    RenderCacheCaptures,
    RejectedRenderCacheCaptures,
    OpaqueBoundaries,
    OpaqueExternalExecutions,
    Opaque3DBoundaries,
    ExecutedOutcomes,
    CachedOutcomes,
    MetadataOutcomes,
    SkippedOutcomes,
    FailedOutcomes,
    Failures,
    CleanupFailures,
    ExternalRootResources,
}
