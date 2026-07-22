using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

internal readonly record struct RenderExecutionStatistics(
    int ShaderRunExecutions,
    int ShaderStageExecutions,
    int FusedShaderRunExecutions,
    int IntermediateTargetAcquisitions,
    int ProgramCacheHits,
    int Synchronizations);

internal sealed class RenderRequestExecutor
{
    private static readonly object s_cpuProgramDevice = new();
    private static readonly object s_cpuProgramContext = new();
    private static readonly object s_defaultCompileOptions = new();

    private readonly RenderTargetLeaseSession _targets;
    private readonly ProgramCache<CachedSkRuntimeEffect>? _programCache;

    public RenderExecutionStatistics Statistics { get; private set; }

    public RenderRequestExecutor(
        RenderTargetLeaseSession targets,
        ProgramCache<CachedSkRuntimeEffect>? programCache = null)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _programCache = programCache;
    }

    public void Execute(
        CompiledRenderRequest request,
        ImmediateCanvas destination,
        Action? finalizeOutput = null,
        Rect? replayBounds = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(request.IsDisposed, request);
        ObjectDisposedException.ThrowIf(destination.IsDisposed, destination);
        ValidateFamilyForExecution(request);

        RenderPipelineDiagnosticRecorder? rootDiagnostics = RenderRequestDiagnostics.TryGet(request.Request);
        rootDiagnostics?.RecordExternalRootResource();
        ProgramCache<CachedSkRuntimeEffect>? localProgramCache = _programCache is null
            ? SkRuntimeEffectProgramCache.Create()
            : null;
        ProgramCache<CachedSkRuntimeEffect> familyProgramCache = _programCache ?? localProgramCache!;
        var frames = new List<FamilyExecutionFrame>();
        var cleanupFailures = new List<Exception>();
        ExceptionDispatchInfo? primaryFailure = null;
        RenderPipelineFailurePhase failurePhase = RenderPipelineFailurePhase.Execution;
        int nestedRootAcquisitions = 0;
        try
        {
            try
            {
                ExecuteFamily(
                    request,
                    destination,
                    replayBounds ?? request.SelectedOutputBounds,
                    finalizeOutput,
                    familyProgramCache,
                    frames,
                    cleanupFailures,
                    ref nestedRootAcquisitions);
            }
            catch (FamilyExecutionException ex)
            {
                primaryFailure = ex.Failure;
                failurePhase = ex.FailurePhase;
            }
            catch (Exception ex)
            {
                rootDiagnostics?.RecordFailure(RenderPipelineFailurePhase.Execution);
                primaryFailure = ExceptionDispatchInfo.Capture(ex);
            }

            if (localProgramCache is not null)
            {
                try
                {
                    localProgramCache.Dispose();
                }
                catch (Exception ex)
                {
                    AppendCleanupFailures(cleanupFailures, rootDiagnostics, ex);
                }
            }

            if (primaryFailure is null && cleanupFailures.Count != 0)
            {
                primaryFailure = ExceptionDispatchInfo.Capture(cleanupFailures[0]);
                failurePhase = RenderPipelineFailurePhase.Cleanup;
            }

            if (primaryFailure is not null)
                RejectNestedBindings(request);

            RenderRequestOwner owner = request.Request.Options.Owner;
            EnsureOwnerPrimary(owner, primaryFailure?.SourceException);
            int ownerCleanupStart = owner.CleanupFailures.Length;
            owner.Cleanup();
            foreach (Exception failure in owner.CleanupFailures.Skip(ownerCleanupStart))
            {
                cleanupFailures.Add(failure);
                rootDiagnostics?.RecordCleanupFailure();
                if (primaryFailure is null)
                {
                    primaryFailure = ExceptionDispatchInfo.Capture(failure);
                    failurePhase = RenderPipelineFailurePhase.Cleanup;
                }
            }

            try
            {
                _targets.ThrowIfCleanupFailed();
            }
            catch (Exception ex)
            {
                AppendCleanupFailures(cleanupFailures, rootDiagnostics, ex);
                if (primaryFailure is null)
                {
                    primaryFailure = ExceptionDispatchInfo.Capture(
                        ex is AggregateException aggregate
                            ? aggregate.Flatten().InnerExceptions[0]
                            : ex);
                    failurePhase = RenderPipelineFailurePhase.Cleanup;
                }
            }

            if (primaryFailure is null)
            {
                try
                {
                    foreach (FamilyExecutionFrame frame in frames)
                        frame.State.PublishBuiltInBackdropCaptures();
                }
                catch (Exception ex)
                {
                    primaryFailure = ExceptionDispatchInfo.Capture(ex);
                    failurePhase = RenderPipelineFailurePhase.Execution;
                }
            }

            if (primaryFailure is null)
            {
                try
                {
                    IReadOnlyList<Exception> publicationCleanupFailures =
                        PublishCacheCapturesAtomically(frames);
                    foreach (Exception failure in publicationCleanupFailures)
                    {
                        cleanupFailures.Add(failure);
                        rootDiagnostics?.RecordCleanupFailure();
                    }
                    if (publicationCleanupFailures.Count != 0)
                    {
                        primaryFailure = ExceptionDispatchInfo.Capture(publicationCleanupFailures[0]);
                        failurePhase = RenderPipelineFailurePhase.Cleanup;
                    }
                }
                catch (FamilyCachePublicationException ex)
                {
                    rootDiagnostics?.RecordFailure(RenderPipelineFailurePhase.CachePublication);
                    foreach (Exception cleanupFailure in ex.CleanupFailures)
                        AppendCleanupFailures(cleanupFailures, rootDiagnostics, cleanupFailure);
                    primaryFailure = ex.Failure;
                    failurePhase = RenderPipelineFailurePhase.CachePublication;
                }
                catch (Exception ex)
                {
                    rootDiagnostics?.RecordFailure(RenderPipelineFailurePhase.CachePublication);
                    primaryFailure = ExceptionDispatchInfo.Capture(ex);
                    failurePhase = RenderPipelineFailurePhase.CachePublication;
                }
            }

            foreach (FamilyExecutionFrame frame in frames)
            {
                try
                {
                    frame.State.RejectCacheCaptures();
                    frame.State.RejectBuiltInBackdropCaptures();
                }
                catch (Exception ex)
                {
                    AppendCleanupFailures(cleanupFailures, frame.Diagnostics, ex);
                    if (primaryFailure is null)
                    {
                        primaryFailure = ExceptionDispatchInfo.Capture(
                            ex is AggregateException aggregate
                                ? aggregate.Flatten().InnerExceptions[0]
                                : ex);
                        failurePhase = RenderPipelineFailurePhase.Cleanup;
                    }
                }
            }

            Statistics = AggregateStatistics(frames, nestedRootAcquisitions);
        }
        finally
        {
            // Every state is explicitly drained above. This fallback only protects
            // future edits that introduce an unexpected coordinator exception.
            foreach (FamilyExecutionFrame frame in frames)
            {
                try
                {
                    frame.State.Dispose();
                }
                catch (Exception ex)
                {
                    AppendCleanupFailures(cleanupFailures, frame.Diagnostics, ex);
                    if (primaryFailure is null)
                    {
                        primaryFailure = ExceptionDispatchInfo.Capture(
                            ex is AggregateException aggregate
                                ? aggregate.Flatten().InnerExceptions[0]
                                : ex);
                        failurePhase = RenderPipelineFailurePhase.Cleanup;
                    }
                }
            }
        }

        if (primaryFailure is not null)
        {
            EnsureOwnerPrimary(request.Request.Options.Owner, primaryFailure.SourceException);
            RecordAdditionalFailures(request.Request.Options.Owner, cleanupFailures);
            FailFamily(request, failurePhase);
            primaryFailure.Throw();
        }

        CompleteFamily(request);
    }

    private void ExecuteFamily(
        CompiledRenderRequest request,
        ImmediateCanvas destination,
        Rect replayBounds,
        Action? finalizeOutput,
        ProgramCache<CachedSkRuntimeEffect> programCache,
        ICollection<FamilyExecutionFrame> frames,
        ICollection<Exception> cleanupFailures,
        ref int nestedRootAcquisitions)
    {
        foreach (CompiledRenderRequest nested in request.NestedRequests)
        {
            ExecuteNested(
                nested,
                destination,
                programCache,
                frames,
                cleanupFailures,
                ref nestedRootAcquisitions);
        }

        ExecuteSingle(
            request,
            destination,
            replayBounds,
            finalizeOutput,
            programCache,
            frames,
            cleanupFailures);
    }

    private void ExecuteNested(
        CompiledRenderRequest request,
        ImmediateCanvas fallbackDestination,
        ProgramCache<CachedSkRuntimeEffect> programCache,
        ICollection<FamilyExecutionFrame> frames,
        ICollection<Exception> cleanupFailures,
        ref int nestedRootAcquisitions)
    {
        RenderPipelineDiagnosticRecorder? diagnostics = RenderRequestDiagnostics.TryGet(request.Request);
        NestedRenderTargetBinding binding = request.Request.Options.TargetBinding
            ?? throw new InvalidOperationException("A nested request has no separate-target binding.");
        bool needsTarget = request.Measurement.HasContributingValues
                           || request.Measurement.HasTargetEffects;
        if (!needsTarget)
        {
            ExecuteFamily(
                request,
                fallbackDestination,
                request.ExecutionTargetBounds,
                finalizeOutput: null,
                programCache,
                frames,
                cleanupFailures,
                ref nestedRootAcquisitions);
            return;
        }

        Rect bounds = request.Request.Options.TargetDomain
            ?? throw new InvalidOperationException(
                "A separate-target nested request requires a finite target domain.");

        RenderTargetLease? lease = null;
        ImmediateCanvas? canvas = null;
        FamilyExecutionException? failure = null;
        bool recordedAcquisition = false;
        int sessionCleanupStart = _targets.CleanupFailures.Count;
        try
        {
            PixelRect deviceBounds = PixelRect.FromRect(bounds, request.Request.Options.OutputScale);
            Rect rasterBounds = deviceBounds.ToRect(request.Request.Options.OutputScale);
            try
            {
                lease = _targets.Acquire(deviceBounds.Size);
                nestedRootAcquisitions++;
                diagnostics?.RecordIntermediateAcquired(
                    created: !lease.WasReused,
                    poolHit: lease.WasReused);
                recordedAcquisition = true;
                RenderTarget target = lease.Target;
                binding.Stage(
                    lease,
                    bounds,
                    request.Request.Options.OutputScale,
                    diagnostics);
                lease = null;
                canvas = ImmediateCanvas.CreateExecutorManaged(
                    target,
                    request.Request.Options.OutputScale,
                    request.Request.Options.MaxWorkingScale,
                    rasterBounds.Size);
                canvas.Clear();
            }
            catch (Exception ex)
            {
                diagnostics?.RecordFailure(RenderPipelineFailurePhase.Allocation);
                failure = new FamilyExecutionException(
                    ExceptionDispatchInfo.Capture(ex),
                    RenderPipelineFailurePhase.Allocation);
            }

            if (failure is null)
            {
                using (canvas!.PushTransform(Matrix.CreateTranslation(
                           -rasterBounds.X,
                           -rasterBounds.Y)))
                {
                    ExecuteFamily(
                        request,
                        canvas,
                        request.ExecutionTargetBounds,
                        finalizeOutput: null,
                        programCache,
                        frames,
                        cleanupFailures,
                        ref nestedRootAcquisitions);
                }

                canvas.CloseWithoutFlush();
                canvas = null;
                binding.PrepareForSampling();
            }
        }
        catch (FamilyExecutionException ex)
        {
            failure = ex;
        }
        finally
        {
            if (failure is not null)
                binding.Reject();

            try
            {
                canvas?.CloseWithoutFlush();
            }
            catch (Exception ex)
            {
                AppendCleanupFailures(cleanupFailures, diagnostics, ex);
                failure ??= new FamilyExecutionException(
                    ExceptionDispatchInfo.Capture(ex),
                    RenderPipelineFailurePhase.Cleanup);
            }

            lease?.Dispose();
            if (lease is not null && recordedAcquisition)
                diagnostics?.RecordIntermediateDischarged();
            foreach (Exception cleanupFailure in _targets.CleanupFailures.Skip(sessionCleanupStart))
            {
                AppendCleanupFailures(cleanupFailures, diagnostics, cleanupFailure);
                failure ??= new FamilyExecutionException(
                    ExceptionDispatchInfo.Capture(cleanupFailure),
                    RenderPipelineFailurePhase.Cleanup);
            }
        }

        if (failure is not null)
            throw failure;
    }

    private void ExecuteSingle(
        CompiledRenderRequest request,
        ImmediateCanvas destination,
        Rect replayBounds,
        Action? finalizeOutput,
        ProgramCache<CachedSkRuntimeEffect> programCache,
        ICollection<FamilyExecutionFrame> frames,
        ICollection<Exception> cleanupFailures)
    {
        RenderPipelineDiagnosticRecorder? diagnostics = RenderRequestDiagnostics.TryGet(request.Request);
        request.Request.TransitionTo(RenderRequestState.Executing);
        var state = new CompatibilityExecutionState(
            request.Request.Options,
            request.Graph,
            request.ExecutionPlan,
            request.TargetDependencies,
            request.Regions,
            request.Roots,
            request.CacheResolution,
            _targets,
            programCache,
            diagnostics);
        var frame = new FamilyExecutionFrame(request, state, diagnostics);
        frames.Add(frame);
        ExceptionDispatchInfo? bodyFailure = null;
        RenderPipelineFailurePhase bodyFailurePhase = RenderPipelineFailurePhase.Execution;
        try
        {
            if (replayBounds.Width != 0 && replayBounds.Height != 0)
            {
                Rect rasterClip = RenderScaleUtilities.AddRasterApron(
                        PixelRect.FromRect(replayBounds, destination.Density))
                    .ToRect(destination.Density);
                using (destination.PushClip(rasterClip))
                {
                    foreach (RenderFragmentReference root in request.Roots)
                        state.Replay(root, destination);
                }
            }
            else
            {
                foreach (RenderFragmentReference root in request.Roots)
                {
                    if (root.HasTargetEffects)
                        state.Replay(root, destination);
                }
            }

            state.ValidateExecutionCompleted(
                allowSkippedIslands: replayBounds.Width == 0 || replayBounds.Height == 0);
            state.PrepareBuiltInBackdropCaptures();
            finalizeOutput?.Invoke();
        }
        catch (Exception ex)
        {
            bodyFailurePhase = state.FailurePhase ?? RenderPipelineFailurePhase.Execution;
            diagnostics?.RecordFailure(bodyFailurePhase, state.ActiveSubjectId);
            bodyFailure = ExceptionDispatchInfo.Capture(ex);
        }

        ExceptionDispatchInfo? cleanupFailure = null;
        try
        {
            state.DisposeNonCacheValues();
        }
        catch (Exception ex)
        {
            AppendCleanupFailures(cleanupFailures, diagnostics, ex);
            cleanupFailure = ExceptionDispatchInfo.Capture(
                ex is AggregateException aggregate
                    ? aggregate.Flatten().InnerExceptions[0]
                    : ex);
        }

        if (bodyFailure is not null)
            throw new FamilyExecutionException(bodyFailure, bodyFailurePhase);
        if (cleanupFailure is not null)
            throw new FamilyExecutionException(cleanupFailure, RenderPipelineFailurePhase.Cleanup);
    }

    private static void ValidateFamilyForExecution(CompiledRenderRequest request)
    {
        foreach (CompiledRenderRequest nested in request.NestedRequests)
            ValidateFamilyForExecution(nested);
        ObjectDisposedException.ThrowIf(request.IsDisposed, request);
        if (request.Request.State != RenderRequestState.Planned)
            throw new InvalidOperationException("Every render request in a family must be planned before execution.");
    }

    private static IReadOnlyList<Exception> PublishCacheCapturesAtomically(
        IReadOnlyList<FamilyExecutionFrame> frames)
    {
        var seenCaches = new HashSet<RenderNodeCache>(ReferenceEqualityComparer.Instance);
        foreach (FamilyExecutionFrame frame in frames)
            frame.State.ValidateCacheCaptures(seenCaches);

        var transferredTargets = new List<RenderTarget>();
        var publications = new List<RenderNodeCachePublication>();
        IReadOnlyList<Exception> replacedStorageCleanupFailures;
        try
        {
            foreach (FamilyExecutionFrame frame in frames)
                frame.State.AppendCachePublications(publications, transferredTargets);
            replacedStorageCleanupFailures = RenderNodeCache.PublishAtomically(publications);
        }
        catch (Exception ex)
        {
            ExceptionDispatchInfo primary = ExceptionDispatchInfo.Capture(ex);
            var cleanupFailures = new List<Exception>();
            for (int index = transferredTargets.Count - 1; index >= 0; index--)
            {
                try
                {
                    transferredTargets[index].Dispose();
                }
                catch (Exception cleanupFailure)
                {
                    cleanupFailures.Add(cleanupFailure);
                }
            }
            throw new FamilyCachePublicationException(primary, cleanupFailures);
        }

        transferredTargets.Clear();
        foreach (FamilyExecutionFrame frame in frames)
            frame.State.AcceptCacheCaptures();
        return replacedStorageCleanupFailures;
    }

    private static RenderExecutionStatistics AggregateStatistics(
        IEnumerable<FamilyExecutionFrame> frames,
        int nestedRootAcquisitions)
    {
        int shaderRuns = 0;
        int shaderStages = 0;
        int fusedRuns = 0;
        int intermediateTargets = nestedRootAcquisitions;
        int programCacheHits = 0;
        int synchronizations = 0;
        foreach (FamilyExecutionFrame frame in frames)
        {
            RenderExecutionStatistics statistics = frame.State.CreateStatistics();
            shaderRuns += statistics.ShaderRunExecutions;
            shaderStages += statistics.ShaderStageExecutions;
            fusedRuns += statistics.FusedShaderRunExecutions;
            intermediateTargets += statistics.IntermediateTargetAcquisitions;
            programCacheHits += statistics.ProgramCacheHits;
            synchronizations += statistics.Synchronizations;
        }

        return new RenderExecutionStatistics(
            shaderRuns,
            shaderStages,
            fusedRuns,
            intermediateTargets,
            programCacheHits,
            synchronizations);
    }

    private static void CompleteFamily(CompiledRenderRequest request)
    {
        foreach (CompiledRenderRequest member in EnumerateFamilyDepthFirst(request))
        {
            member.Request.TransitionTo(RenderRequestState.Completed);
            RenderRequestDiagnostics.Complete(member.Request);
        }
    }

    private static void RejectNestedBindings(CompiledRenderRequest request)
    {
        foreach (CompiledRenderRequest member in EnumerateFamilyDepthFirst(request))
            member.Request.Options.TargetBinding?.Reject();
    }

    private static void FailFamily(
        CompiledRenderRequest request,
        RenderPipelineFailurePhase failurePhase)
    {
        foreach (CompiledRenderRequest member in EnumerateFamilyDepthFirst(request))
        {
            RenderPipelineDiagnosticRecorder? diagnostics = RenderRequestDiagnostics.TryGet(member.Request);
            diagnostics?.RecordFamilyFailure(failurePhase);
            member.Request.FailFamilyMember();
            RenderRequestDiagnostics.Complete(member.Request);
        }
    }

    private static IEnumerable<CompiledRenderRequest> EnumerateFamilyDepthFirst(
        CompiledRenderRequest request)
    {
        foreach (CompiledRenderRequest nested in request.NestedRequests)
        {
            foreach (CompiledRenderRequest member in EnumerateFamilyDepthFirst(nested))
                yield return member;
        }

        yield return request;
    }

    private static void EnsureOwnerPrimary(RenderRequestOwner owner, Exception? failure)
    {
        if (failure is not null && owner.PrimaryFailure is null)
            owner.RecordPrimaryFailure(failure);
    }

    private sealed record FamilyExecutionFrame(
        CompiledRenderRequest Request,
        CompatibilityExecutionState State,
        RenderPipelineDiagnosticRecorder? Diagnostics);

    private sealed class FamilyExecutionException(
        ExceptionDispatchInfo failure,
        RenderPipelineFailurePhase failurePhase) : Exception
    {
        public ExceptionDispatchInfo Failure { get; } = failure;

        public RenderPipelineFailurePhase FailurePhase { get; } = failurePhase;
    }

    private sealed class FamilyCachePublicationException(
        ExceptionDispatchInfo failure,
        IReadOnlyList<Exception> cleanupFailures) : Exception
    {
        public ExceptionDispatchInfo Failure { get; } = failure;

        public IReadOnlyList<Exception> CleanupFailures { get; } = cleanupFailures;
    }

    private static void AppendCleanupFailures(
        ICollection<Exception> failures,
        RenderPipelineDiagnosticRecorder? diagnostics,
        Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            foreach (Exception inner in aggregate.Flatten().InnerExceptions)
            {
                AddCleanupFailure(failures, diagnostics, inner);
            }
        }
        else
        {
            AddCleanupFailure(failures, diagnostics, exception);
        }
    }

    private static void AddCleanupFailure(
        ICollection<Exception> failures,
        RenderPipelineDiagnosticRecorder? diagnostics,
        Exception exception)
    {
        if (failures.Any(existing => ReferenceEquals(existing, exception)))
            return;
        failures.Add(exception);
        diagnostics?.RecordCleanupFailure();
    }

    private static void RecordAdditionalFailures(
        RenderRequestOwner owner,
        IEnumerable<Exception> failures)
    {
        Exception[] ownerCleanupFailures = [.. owner.CleanupFailures];
        foreach (Exception failure in failures)
        {
            if (!ReferenceEquals(owner.PrimaryFailure?.SourceException, failure)
                && !ownerCleanupFailures.Any(existing => ReferenceEquals(existing, failure)))
            {
                owner.RecordPrimaryFailure(failure);
            }
        }
    }

    private sealed class CompatibilityExecutionState : IDisposable
    {
        private readonly RenderRequestOptions _options;
        private readonly ExecutionIslandPlan _executionPlan;
        private readonly ExecutionIslandExecutionLedger _executionLedger;
        private readonly RegionAnalysis _regions;
        private readonly ResourcePlanUseTracker _resourceUses;
        private readonly RenderCacheResolution _cacheResolution;
        private readonly HashSet<RenderFragmentReference> _roots;
        private readonly RenderTargetLeaseSession _targets;
        private readonly ProgramCache<CachedSkRuntimeEffect> _programCache;
        private readonly RenderPipelineDiagnosticRecorder? _diagnostics;
        private readonly Dictionary<RenderFragmentId, Rect> _resolvedScopeDomains = [];
        private readonly Dictionary<RenderFragmentId, Rect> _resolvedParentScopeDomains = [];
        private readonly Dictionary<RenderFragmentId, Rect> _resolvedAccessDomains = [];
        private readonly Dictionary<RenderFragmentReference, IReadOnlyList<CompatibilityRenderValue>> _values =
            new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<CompatibilityRenderValue> _ownedValues =
            new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<CompatibilityRenderValue> _diagnosticIntermediates =
            new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<CompatibilityRenderValue> _cacheCaptureValues =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<CompatibilityRenderValue, int> _valueReferences =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<RenderFragmentId, RenderCacheHitSubstitution> _cacheHits;
        private readonly Dictionary<RenderFragmentId, ImmutableArray<RenderCacheMissCapture>> _cacheMisses;
        private readonly HashSet<RenderFragmentId> _skippedExecutionSubjects = [];
        private readonly List<PendingRenderCacheCapture> _pendingCacheCaptures = [];
        private readonly List<(IBuiltInBackdropCaptureSink Sink, CompatibilityRenderValue Value)> _backdropCaptures = [];
        private readonly List<PendingBackdropPublication> _pendingBackdropPublications = [];
        private int _shaderRunExecutions;
        private int _shaderStageExecutions;
        private int _fusedShaderRunExecutions;
        private int _intermediateTargetAcquisitions;
        private int _programCacheHits;
        private int _synchronizations;

        public long? ActiveSubjectId { get; private set; }

        public RenderPipelineFailurePhase? FailurePhase { get; private set; }

        public CompatibilityExecutionState(
            RenderRequestOptions options,
            RecordedRenderGraph graph,
            ExecutionIslandPlan executionPlan,
            TargetDependencyPlan targetDependencies,
            RegionAnalysis regions,
            ImmutableArray<RenderFragmentReference> roots,
            RenderCacheResolution cacheResolution,
            RenderTargetLeaseSession targets,
            ProgramCache<CachedSkRuntimeEffect> programCache,
            RenderPipelineDiagnosticRecorder? diagnostics)
        {
            _options = options;
            _executionPlan = executionPlan;
            _executionLedger = executionPlan.CreateExecutionLedger(graph, roots, cacheResolution);
            _regions = regions;
            var cacheHitFragmentIds = cacheResolution.Hits
                .Select(static hit => hit.OriginalProducerId)
                .ToHashSet();
            _resourceUses = ResourcePlan.CreateUseSchedule(roots, cacheHitFragmentIds).BeginExecution();
            _cacheResolution = cacheResolution;
            _roots = new HashSet<RenderFragmentReference>(
                roots,
                ReferenceEqualityComparer.Instance);
            _targets = targets;
            _programCache = programCache;
            _diagnostics = diagnostics;
            _cacheHits = cacheResolution.Hits.ToDictionary(static item => item.OriginalProducerId);
            _cacheMisses = cacheResolution.MissCaptures
                .GroupBy(static item => item.ProducerId)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.ToImmutableArray());

            var scopes = targetDependencies.Scopes.ToDictionary(static scope => scope.Id);
            foreach (TargetScopePlan scope in targetDependencies.Scopes)
            {
                if (scope.OwnerFragmentId is { } owner && scope.ResolvedDomain is { } domain)
                    AddResolvedDomain(_resolvedScopeDomains, owner, domain);
                if (scope.OwnerFragmentId is { } parentOwner
                    && scope.ParentId is { } parentId
                    && scopes[parentId].ResolvedDomain is { } parentDomain)
                {
                    AddResolvedDomain(_resolvedParentScopeDomains, parentOwner, parentDomain);
                }
            }

            foreach (TargetDependencyStep step in targetDependencies.Steps)
            {
                if (scopes[step.ScopeId].ResolvedDomain is { } domain)
                    AddResolvedDomain(_resolvedAccessDomains, step.FragmentId, domain);
            }
        }

        private void RecordFailure(RenderPipelineFailurePhase phase, long? subjectId)
        {
            FailurePhase ??= phase;
            _diagnostics?.RecordFailure(phase, subjectId);
        }

        private void RecordSynchronization(RenderFragmentReference fragment)
        {
            RenderFragmentId id = fragment.Id
                ?? throw new InvalidOperationException("A synchronizing fragment is not committed.");
            _synchronizations = checked(_synchronizations + 1);
            _diagnostics?.RecordSynchronizationExecuted(id.Value);
        }

        public void Replay(RenderFragmentReference fragment, ImmediateCanvas destination)
        {
            long? previous = ActiveSubjectId;
            ActiveSubjectId = fragment.Id?.Value;
            try
            {
                ReplayCore(fragment, destination);
                if (fragment.Id is { } id
                    && !_cacheHits.ContainsKey(id)
                    && !_skippedExecutionSubjects.Contains(id))
                {
                    _diagnostics?.RecordFragmentExecuted(id.Value);
                }
                CompleteFragmentUse(fragment);
            }
            catch
            {
                RecordFailure(RenderPipelineFailurePhase.Execution, fragment.Id?.Value);
                throw;
            }
            finally
            {
                ActiveSubjectId = previous;
            }
        }

        private void ReplayCore(RenderFragmentReference fragment, ImmediateCanvas destination)
        {
            if (fragment.Id is { } boundaryId
                && (_cacheHits.ContainsKey(boundaryId) || _cacheMisses.ContainsKey(boundaryId)))
            {
                IReadOnlyList<CompatibilityRenderValue> boundaryValues = Materialize(
                    fragment,
                    destination,
                    fragment.EffectiveScale.IsUnbounded
                        ? EffectiveScale.At(destination.Density)
                        : null);
                if (fragment.ContributesValuesToTarget)
                    DrawValues(boundaryValues, destination);
                return;
            }

            if (_executionPlan.TryGetMembership(fragment, out ExecutionIslandMembership membership)
                && membership.ShaderRun is not null)
            {
                if (TryExecuteCompiledShaderRunDirect(
                        fragment,
                        membership.ShaderRun,
                        destination))
                {
                    return;
                }
                DrawMaterializedFragment(fragment, destination);
                return;
            }

            switch (fragment.Kind)
            {
                case RenderFragmentKind.ContributeValues:
                    DrawValues(
                        MaterializeSingleInput(
                            fragment,
                            destination,
                            fragment.EffectiveScale.IsUnbounded
                                ? EffectiveScale.At(destination.Density)
                                : null),
                        destination);
                    return;
                case RenderFragmentKind.Opacity:
                    ExecuteReplayIsland(
                        fragment,
                        () =>
                        {
                            using (ObserveGpuPass(fragment))
                            using (destination.PushOpacity(((OpacityRenderFragmentPayload)fragment.Payload!).Opacity))
                                Replay(fragment.Inputs.Single(), destination);
                        });
                    return;
                case RenderFragmentKind.Blend:
                    ExecuteReplayIsland(
                        fragment,
                        () =>
                        {
                            using (ObserveGpuPass(fragment))
                            using (destination.PushBlendMode(((BlendRenderFragmentPayload)fragment.Payload!).BlendMode))
                                Replay(fragment.Inputs.Single(), destination);
                        });
                    return;
                case RenderFragmentKind.OpacityMask:
                    ExecuteReplayIsland(fragment, () => ReplayOpacityMask(fragment, destination));
                    return;
                case RenderFragmentKind.Layer:
                    if (fragment.ContributesValuesToTarget)
                        DrawValues(
                            Materialize(fragment, destination, EffectiveScale.At(destination.Density)),
                            destination);
                    else
                        _ = Materialize(fragment, destination, EffectiveScale.At(destination.Density));
                    return;
                case RenderFragmentKind.TargetLayerScope:
                    ExecuteReplayIsland(fragment, () => ReplayTargetLayerScope(fragment, destination));
                    return;
                case RenderFragmentKind.OpaqueSource:
                    if (TryReplayEngineSourceDirect(fragment, destination))
                        return;
                    DrawMaterializedFragment(fragment, destination);
                    return;
                case RenderFragmentKind.OpaqueMap:
                case RenderFragmentKind.OpaqueExpand:
                case RenderFragmentKind.LegacyFilterEffect:
                case RenderFragmentKind.MaterializedInput:
                case RenderFragmentKind.Shader:
                case RenderFragmentKind.Geometry:
                    DrawMaterializedFragment(fragment, destination);
                    return;
                case RenderFragmentKind.OpaqueCombine:
                    if (TryReplayEngineSourceDirect(fragment, destination))
                        return;
                    DrawMaterializedFragment(fragment, destination);
                    return;
                case RenderFragmentKind.TargetCapture:
                    _ = Materialize(fragment, destination);
                    return;
                case RenderFragmentKind.BuiltInBackdropCapture:
                    {
                        IReadOnlyList<CompatibilityRenderValue> values = Materialize(fragment, destination);
                        if (values.Count != 1
                            || ((BuiltInBackdropCaptureRenderFragmentPayload)fragment.Payload!).Identity
                            is not IBuiltInBackdropCaptureSink sink)
                        {
                            throw new InvalidOperationException(
                                "A built-in backdrop capture must produce one value for its publication sink.");
                        }

                        AddValueReferences(values);
                        _backdropCaptures.Add((sink, values[0]));
                        return;
                    }
                case RenderFragmentKind.TargetCommand:
                    ExecuteReplayIsland(fragment, () => ExecuteTargetCommand(fragment, destination));
                    return;
                case RenderFragmentKind.RawTargetCommand:
                    ExecuteReplayIsland(fragment, () => ExecuteRawTargetCommand(fragment, destination));
                    return;
                case RenderFragmentKind.TargetScope:
                    ExecuteReplayIsland(fragment, () => ExecuteTargetScope(fragment, destination));
                    return;
                case RenderFragmentKind.RawTargetScope:
                    ExecuteReplayIsland(fragment, () => ExecuteRawTargetScope(fragment, destination));
                    return;
                default:
                    throw new InvalidOperationException("The recorded render-fragment kind is invalid.");
            }
        }

        private bool TryReplayEngineSourceDirect(
            RenderFragmentReference fragment,
            ImmediateCanvas destination)
        {
            OpaqueRenderDescription description =
                ((OpaqueRenderFragmentPayload)fragment.Payload!).Description;
            if (description.DirectReplay is not { } replay
                || !fragment.ContributesValuesToTarget
                || _values.ContainsKey(fragment)
                || fragment.Id is { } id
                    && (_cacheHits.ContainsKey(id) || _cacheMisses.ContainsKey(id))
                || _resourceUses.GetRemainingUseCount(fragment) != 1)
            {
                return false;
            }

            var inputs = new List<CompatibilityRenderValue>();
            EffectiveScale outputSupply = fragment.EffectiveScale.IsUnbounded
                ? EffectiveScale.At(destination.Density)
                : fragment.EffectiveScale;
            try
            {
                foreach (RenderFragmentReference input in fragment.Inputs)
                {
                    inputs.AddRange(Materialize(
                        input,
                        destination,
                        input.EffectiveScale.IsUnbounded ? outputSupply : null));
                }

                ExecuteReplayIsland(
                    fragment,
                    () =>
                    {
                        var token = new RenderExecutionSessionToken();
                        var images = new List<SKImage>();
                        try
                        {
                            IReadOnlyList<RenderExecutionInput> executionInputs = CreateExecutionInputs(
                                token,
                                inputs,
                                requiresReadback: false,
                                readbackOwner: null,
                                images);
                            using (ObserveGpuPass(fragment))
                            {
                                replay(new EngineDirectRenderSession(
                                    token,
                                    destination,
                                    executionInputs,
                                    description.Resources));
                            }
                        }
                        finally
                        {
                            try
                            {
                                token.Complete();
                            }
                            finally
                            {
                                foreach (SKImage image in images.AsEnumerable().Reverse())
                                    image.Dispose();
                            }
                        }
                    });
                return true;
            }
            finally
            {
                foreach (RenderFragmentReference input in fragment.Inputs)
                    CompleteFragmentUse(input);
            }
        }

        private bool TryExecuteCompiledShaderRunDirect(
            RenderFragmentReference fragment,
            CompiledShaderRun run,
            ImmediateCanvas destination)
        {
            if (!ReferenceEquals(run.Output, fragment)
                || !_roots.Contains(fragment)
                || !fragment.ContributesValuesToTarget
                || _values.ContainsKey(fragment)
                || fragment.Id is { } id
                    && (_cacheHits.ContainsKey(id) || _cacheMisses.ContainsKey(id))
                || _resourceUses.GetRemainingUseCount(fragment) != 1)
            {
                return false;
            }

            Rect outputBounds = run.Output.Bounds;
            Rect requiredRegion = ResolveFragmentRequirement(run.Output, outputBounds);
            if (requiredRegion.Width == 0 || requiredRegion.Height == 0)
                return false;

            float requestedDensity = fragment.EffectiveScale.IsUnbounded
                ? destination.Density
                : fragment.EffectiveScale.Value;
            float density = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(
                outputBounds,
                requestedDensity);
            if (density != destination.Density)
                return false;

            EffectiveScale inputRequestScale = !run.Output.EffectiveScale.IsUnbounded
                ? run.Output.EffectiveScale
                : EffectiveScale.At(destination.Density);
            IReadOnlyList<CompatibilityRenderValue> inputs = Materialize(
                run.Input,
                destination,
                run.Input.EffectiveScale.IsUnbounded ? inputRequestScale : null);
            try
            {
                if (inputs.Count != 1)
                {
                    if (inputs.Count == 0)
                    {
                        ExecutionIsland island = _executionLedger.Begin(fragment);
                        _executionLedger.Complete(island);
                        MarkExecutionSkipped(fragment);
                        return true;
                    }

                    throw new InvalidOperationException(
                        "A directly executed compiled Shader run requires exactly one materialized input.");
                }

                CompatibilityRenderValue input = inputs[0];
                PixelRect outputDeviceBounds = PixelRect.FromRect(requiredRegion, density);
                Rect rasterBounds = outputDeviceBounds.ToRect(density);
                ExecuteReplayIsland(
                    fragment,
                    () => ExecuteCompiledShaderRunProgram(
                        run,
                        input,
                        outputBounds,
                        requiredRegion,
                        outputDeviceBounds,
                        density,
                        shader =>
                        {
                            using SKShader mapped = shader.WithLocalMatrix(
                                SKMatrix.CreateScaleTranslation(
                                    1f / density,
                                    1f / density,
                                    outputDeviceBounds.X / density,
                                    outputDeviceBounds.Y / density));
                            using var paint = new SKPaint
                            {
                                Shader = mapped,
                                IsAntialias = false,
                            };
                            destination.VerifyAccess();
                            destination.Canvas.DrawRect(rasterBounds.ToSKRect(), paint);
                        }));
                return true;
            }
            finally
            {
                CompleteFragmentUse(run.Input);
            }
        }

        private void DrawMaterializedFragment(
            RenderFragmentReference fragment,
            ImmediateCanvas destination)
        {
            IReadOnlyList<CompatibilityRenderValue> values = Materialize(
                fragment,
                destination,
                fragment.EffectiveScale.IsUnbounded
                    ? EffectiveScale.At(destination.Density)
                    : null);
            if (fragment.ContributesValuesToTarget)
                DrawValues(values, destination);
        }

        private void ExecuteReplayIsland(RenderFragmentReference fragment, Action execute)
        {
            ExecutionIsland island = _executionLedger.Begin(fragment);
            execute();
            _executionLedger.Complete(island);
        }

        public void DisposeNonCacheValues()
        {
            try
            {
                DisposeValues(static (_, isCapture) => !isCapture);
            }
            finally
            {
                _values.Clear();
                _valueReferences.Clear();
                _backdropCaptures.Clear();
            }
        }

        public void RejectCacheCaptures()
        {
            try
            {
                DisposeValues(static (_, isCapture) => isCapture);
            }
            finally
            {
                _pendingCacheCaptures.Clear();
                _cacheCaptureValues.Clear();
            }
        }

        public void ValidateCacheCaptures(ISet<RenderNodeCache> seenCaches)
        {
            ArgumentNullException.ThrowIfNull(seenCaches);
            if (_pendingCacheCaptures.Count != _cacheResolution.MissCaptures.Length)
            {
                throw new InvalidOperationException(
                    "Every selected render-cache miss must materialize exactly one staged capture.");
            }

            var byCandidate = _pendingCacheCaptures.ToDictionary(static item => item.Descriptor.CandidateId);
            foreach (RenderCacheMissCapture descriptor in _cacheResolution.MissCaptures)
            {
                if (!byCandidate.ContainsKey(descriptor.CandidateId))
                    throw new InvalidOperationException("A selected render-cache miss was not staged.");
                RenderNodeCache cache = _cacheResolution.GetDecision(descriptor.CandidateId).Candidate.Cache
                    ?? throw new InvalidOperationException("A production cache capture has no node-cache owner.");
                ObjectDisposedException.ThrowIf(cache.IsDisposed, cache);
                if (!seenCaches.Add(cache))
                {
                    throw new InvalidOperationException(
                        "One request family cannot atomically publish two independent outputs to the same node cache.");
                }
            }
        }

        public void AppendCachePublications(
            ICollection<RenderNodeCachePublication> publications,
            ICollection<RenderTarget> transferredTargets)
        {
            ArgumentNullException.ThrowIfNull(publications);
            ArgumentNullException.ThrowIfNull(transferredTargets);
            var byCandidate = _pendingCacheCaptures.ToDictionary(static item => item.Descriptor.CandidateId);
            foreach (RenderCacheMissCapture descriptor in _cacheResolution.MissCaptures)
            {
                PendingRenderCacheCapture pending = byCandidate[descriptor.CandidateId];
                RenderNodeCache cache = _cacheResolution.GetDecision(descriptor.CandidateId).Candidate.Cache!;
                var cachedValues = new List<RenderNodeCachedValue>(pending.Values.Count);
                foreach (CompatibilityRenderValue value in pending.Values)
                {
                    RenderTarget target = value.TransferToAcceptedCache();
                    transferredTargets.Add(target);
                    cachedValues.Add(new RenderNodeCachedValue(
                        target,
                        value.Bounds,
                        value.EffectiveScale,
                        value.DeviceBounds)
                    {
                        CompleteBounds = value.CompleteBounds,
                    });
                    _ownedValues.Remove(value);
                    _cacheCaptureValues.Remove(value);
                    if (_diagnosticIntermediates.Remove(value))
                        _diagnostics?.RecordIntermediateDischarged();
                }

                publications.Add(new RenderNodeCachePublication(
                    cache,
                    descriptor.Identity,
                    cachedValues));
            }
        }

        public void AcceptCacheCaptures()
        {
            _pendingCacheCaptures.Clear();
            _diagnostics?.CommitAcceptedCacheCaptures();
        }

        public void Dispose()
        {
            var failures = new List<Exception>();
            try
            {
                DisposeValues(static (_, _) => true);
            }
            catch (AggregateException aggregate)
            {
                failures.AddRange(aggregate.Flatten().InnerExceptions);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
            finally
            {
                _pendingCacheCaptures.Clear();
                _cacheCaptureValues.Clear();
            }

            try
            {
                RejectBuiltInBackdropCaptures();
            }
            catch (AggregateException aggregate)
            {
                failures.AddRange(aggregate.Flatten().InnerExceptions);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }

            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1)
                throw new AggregateException("One or more execution-state resources failed to dispose.", failures);
        }

        private void DisposeValues(Func<CompatibilityRenderValue, bool, bool> predicate)
        {
            List<Exception>? failures = null;
            foreach (CompatibilityRenderValue value in _ownedValues.Reverse().ToArray())
            {
                bool isCapture = _cacheCaptureValues.Contains(value);
                if (!predicate(value, isCapture))
                    continue;

                _ownedValues.Remove(value);
                _cacheCaptureValues.Remove(value);
                try
                {
                    DisposeOwnedValue(value);
                }
                catch (Exception ex)
                {
                    (failures ??= []).Add(ex);
                }
            }

            if (failures is null)
                return;
            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new AggregateException("One or more render values failed to dispose.", failures);
        }

        public RenderExecutionStatistics CreateStatistics()
            => new(
                _shaderRunExecutions,
                _shaderStageExecutions,
                _fusedShaderRunExecutions,
                _intermediateTargetAcquisitions,
                _programCacheHits,
                _synchronizations);

        public void ValidateExecutionCompleted(bool allowSkippedIslands)
            => _executionLedger.ValidateCompleted(allowSkippedIslands);

        public void PrepareBuiltInBackdropCaptures()
        {
            var prepared = new List<PendingBackdropPublication>(_backdropCaptures.Count);
            try
            {
                foreach ((IBuiltInBackdropCaptureSink sink, CompatibilityRenderValue value) in _backdropCaptures)
                {
                    Bitmap bitmap = value.Target.Snapshot();
                    var publication = new PendingBackdropPublication(
                        sink,
                        bitmap,
                        value.EffectiveScale.Value);
                    prepared.Add(publication);
                    _pendingBackdropPublications.Add(publication);
                }
            }
            catch
            {
                foreach (PendingBackdropPublication publication in prepared)
                {
                    publication.Bitmap?.Dispose();
                    publication.Bitmap = null;
                    _pendingBackdropPublications.Remove(publication);
                }
                throw;
            }
            finally
            {
                foreach (CompatibilityRenderValue value in _backdropCaptures.Select(static item => item.Value))
                    ReleaseValueReference(value);
                _backdropCaptures.Clear();
            }
        }

        public void PublishBuiltInBackdropCaptures()
        {
            foreach (PendingBackdropPublication publication in _pendingBackdropPublications)
            {
                Bitmap bitmap = publication.Bitmap
                    ?? throw new InvalidOperationException("A backdrop capture was already discharged.");
                try
                {
                    publication.Sink.CommitBackdropCapture(bitmap, publication.Density);
                    publication.Bitmap = null;
                }
                catch
                {
                    bitmap.Dispose();
                    publication.Bitmap = null;
                    throw;
                }
            }

            _pendingBackdropPublications.Clear();
        }

        public void RejectBuiltInBackdropCaptures()
        {
            List<Exception>? failures = null;
            foreach (PendingBackdropPublication publication in _pendingBackdropPublications)
            {
                Bitmap? bitmap = publication.Bitmap;
                publication.Bitmap = null;
                if (bitmap is null)
                    continue;
                try
                {
                    bitmap.Dispose();
                }
                catch (Exception ex)
                {
                    (failures ??= []).Add(ex);
                }
            }
            _pendingBackdropPublications.Clear();

            if (failures is null)
                return;
            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new AggregateException("One or more staged backdrop captures failed to dispose.", failures);
        }

        private IReadOnlyList<CompatibilityRenderValue> Materialize(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale = null)
        {
            long? previous = ActiveSubjectId;
            ActiveSubjectId = fragment.Id?.Value;
            try
            {
                IReadOnlyList<CompatibilityRenderValue> result = MaterializeCore(
                    fragment,
                    currentTarget,
                    requestedScale);
                if (fragment.Id is { } id
                    && !_cacheHits.ContainsKey(id)
                    && !_skippedExecutionSubjects.Contains(id))
                {
                    _diagnostics?.RecordFragmentExecuted(id.Value);
                }
                return result;
            }
            catch
            {
                RecordFailure(RenderPipelineFailurePhase.Execution, fragment.Id?.Value);
                throw;
            }
            finally
            {
                ActiveSubjectId = previous;
            }
        }

        private IReadOnlyList<CompatibilityRenderValue> MaterializeCore(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale = null)
        {
            if (_values.TryGetValue(fragment, out IReadOnlyList<CompatibilityRenderValue>? cached))
                return cached;

            IReadOnlyList<CompatibilityRenderValue> result;
            if (TryMaterializeCacheHit(fragment, out IReadOnlyList<CompatibilityRenderValue>? hitValues))
            {
                result = hitValues!;
            }
            else if (_executionPlan.TryGetMembership(fragment, out ExecutionIslandMembership membership))
            {
                ExecutionIsland island = _executionLedger.Begin(fragment);
                result = membership.ShaderRun is { } run
                    ? ExecuteCompiledShaderRun(run, currentTarget, requestedScale)
                    : MaterializePlannedFragment(fragment, currentTarget, requestedScale);
                _executionLedger.Complete(island);
            }
            else
            {
                result = fragment.Kind switch
                {
                    RenderFragmentKind.MaterializedInput => MaterializeExternal(fragment),
                    RenderFragmentKind.ContributeValues => MaterializeSingleInput(fragment, currentTarget),
                    _ => throw new InvalidOperationException(
                        $"Executable fragment '{fragment.Kind}' is not assigned to an execution island."),
                };
            }
            StageCacheCaptures(fragment, result);
            _values.Add(fragment, result);
            AddValueReferences(result);
            if (fragment.Kind == RenderFragmentKind.ContributeValues)
                CompleteFragmentUse(fragment.Inputs.Single());
            return result;
        }

        private IReadOnlyList<CompatibilityRenderValue> MaterializePlannedFragment(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
            => fragment.Kind switch
            {
                RenderFragmentKind.OpaqueSource
                    or RenderFragmentKind.OpaqueMap
                    or RenderFragmentKind.OpaqueCombine
                    or RenderFragmentKind.OpaqueExpand => ExecuteOpaque(fragment, currentTarget, requestedScale),
                RenderFragmentKind.LegacyFilterEffect => ExecuteLegacyFilter(fragment, currentTarget),
                RenderFragmentKind.Shader => ExecuteShader(fragment, currentTarget, requestedScale),
                RenderFragmentKind.Geometry => ExecuteGeometry(fragment, currentTarget),
                RenderFragmentKind.Opacity => MaterializeOpacity(fragment, requestedScale),
                RenderFragmentKind.OpacityMask => MaterializeOpacityMask(fragment, requestedScale),
                RenderFragmentKind.Layer => MaterializeLayer(fragment, requestedScale),
                RenderFragmentKind.TargetCapture
                    or RenderFragmentKind.BuiltInBackdropCapture => CaptureTarget(fragment, currentTarget),
                RenderFragmentKind.TargetScope
                    when ((TargetScopeRenderFragmentPayload)fragment.Payload!).Description.IsValueReplayMap
                    => MaterializeValueReplayMap(fragment, currentTarget, requestedScale),
                _ => throw new NotSupportedException(
                    $"The planned fragment '{fragment.Kind}' cannot be materialized as a value."),
            };

        private IReadOnlyList<CompatibilityRenderValue> MaterializeSingleInput(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale = null)
        {
            if (fragment.Inputs.Length != 1)
                throw new InvalidOperationException("A unary recorded fragment requires exactly one input.");
            return Materialize(fragment.Inputs[0], currentTarget, requestedScale);
        }

        private bool TryMaterializeCacheHit(
            RenderFragmentReference fragment,
            out IReadOnlyList<CompatibilityRenderValue>? values)
        {
            if (fragment.Id is not { } id || !_cacheHits.TryGetValue(id, out RenderCacheHitSubstitution? hit))
            {
                values = null;
                return false;
            }

            if (hit.Entry.Payload is not RenderNodeCachedOutput cachedOutput)
            {
                throw new InvalidOperationException(
                    "A selected render-cache hit does not contain a node-cache output payload.");
            }

            var acquired = new List<CompatibilityRenderValue>(cachedOutput.Values.Count);
            try
            {
                foreach (RenderNodeCachedValue cached in cachedOutput.Values)
                {
                    var value = new CompatibilityRenderValue(
                        cached.Target.ShallowCopy(),
                        cached.Bounds,
                        cached.EffectiveScale,
                        cached.DeviceBounds,
                        ownsTarget: true,
                        completeBounds: cached.CompleteBounds)
                    {
                        PreferPixelExactComposite = true,
                    };
                    _ownedValues.Add(value);
                    acquired.Add(value);
                }
            }
            catch
            {
                foreach (CompatibilityRenderValue value in acquired)
                    ReleaseUnpublished(value);
                throw;
            }

            _diagnostics?.RecordOutcome(id.Value, RenderPipelineOutcome.Cached);
            values = acquired;
            return true;
        }

        private void StageCacheCaptures(
            RenderFragmentReference fragment,
            IReadOnlyList<CompatibilityRenderValue> values)
        {
            if (fragment.Id is not { } id || !_cacheMisses.TryGetValue(id, out var misses))
                return;

            foreach (RenderCacheMissCapture miss in misses)
            {
                var captures = new List<CompatibilityRenderValue>(values.Count);
                try
                {
                    foreach (CompatibilityRenderValue value in values)
                    {
                        value.PreferPixelExactComposite = true;
                        CompatibilityRenderValue capture = CopyForCacheCapture(value);
                        capture.PreferPixelExactComposite = true;
                        _cacheCaptureValues.Add(capture);
                        captures.Add(capture);
                    }

                    _pendingCacheCaptures.Add(new PendingRenderCacheCapture(miss, captures));
                    _diagnostics?.RecordCacheCaptureStaged(miss.ProducerId.Value);
                }
                catch
                {
                    foreach (CompatibilityRenderValue capture in captures)
                    {
                        _cacheCaptureValues.Remove(capture);
                        ReleaseUnpublished(capture);
                    }
                    throw;
                }
            }
        }

        private CompatibilityRenderValue CopyForCacheCapture(CompatibilityRenderValue source)
        {
            CompatibilityRenderValue capture = CreateOwnedValue(
                source.Bounds,
                source.EffectiveScale,
                source.CompleteBounds,
                source.DeviceBounds);
            bool succeeded = false;
            try
            {
                using var canvas = ImmediateCanvas.CreateExecutorManaged(
                    capture.Target,
                    capture.EffectiveScale.Value,
                    _options.MaxWorkingScale,
                    capture.RasterBounds.Size);
                canvas.DrawRenderTargetPixelsWithoutFlush(source.Target, 0, 0);
                succeeded = true;
                return capture;
            }
            finally
            {
                if (!succeeded)
                    ReleaseUnpublished(capture);
            }
        }

        private void ReplayOpacityMask(
            RenderFragmentReference fragment,
            ImmediateCanvas destination)
        {
            if (fragment.Inputs.Length == 0)
                throw new InvalidOperationException("An opacity mask requires a primary input.");

            var payload = (OpacityMaskRenderFragmentPayload)fragment.Payload!;
            var values = new List<CompatibilityRenderValue>();
            for (int index = 1; index < fragment.Inputs.Length; index++)
            {
                values.AddRange(Materialize(
                    fragment.Inputs[index],
                    destination,
                    EffectiveScale.At(destination.Density)));
            }

            var token = new RenderExecutionSessionToken();
            var images = new List<SKImage>();
            try
            {
                IReadOnlyList<RenderExecutionInput> inputs = CreateExecutionInputs(
                    token,
                    values,
                    requiresReadback: false,
                    readbackOwner: null,
                    images);
                BrushExecutionResolver.UseBrush(
                    token,
                    payload.Resources,
                    inputs,
                    payload.Mask,
                    mask =>
                    {
                        using (ObserveGpuPass(fragment))
                        using (destination.PushOpacityMask(mask, payload.BrushBounds, payload.Invert))
                            Replay(fragment.Inputs[0], destination);
                    });
            }
            finally
            {
                foreach (SKImage image in images)
                    image.Dispose();
                token.Complete();
                for (int index = 1; index < fragment.Inputs.Length; index++)
                    CompleteFragmentUse(fragment.Inputs[index]);
            }
        }

        private IReadOnlyList<CompatibilityRenderValue> MaterializeOpacity(
            RenderFragmentReference fragment,
            EffectiveScale? requestedScale)
        {
            if (fragment.Inputs.Length != 1)
                throw new InvalidOperationException("An opacity fragment requires exactly one input.");
            if (fragment.Bounds.Width == 0 || fragment.Bounds.Height == 0)
            {
                CompleteFragmentUse(fragment.Inputs[0]);
                MarkExecutionSkipped(fragment);
                return [];
            }

            EffectiveScale scale = requestedScale ?? ResolveConcreteScale(fragment, fragment.Bounds);
            CompatibilityRenderValue value = CreateOwnedValue(fragment.Bounds, scale);
            _diagnostics?.RecordGpuPassExecuted(fragment.Id?.Value ?? 0);
            bool succeeded = false;
            try
            {
                using var canvas = ImmediateCanvas.CreateExecutorManaged(
                    value.Target,
                    scale.Value,
                    _options.MaxWorkingScale,
                    value.RasterBounds.Size);
                using (canvas.PushTransform(Matrix.CreateTranslation(
                           -value.RasterBounds.X,
                           -value.RasterBounds.Y)))
                using (canvas.PushOpacity(((OpacityRenderFragmentPayload)fragment.Payload!).Opacity))
                    Replay(fragment.Inputs[0], canvas);
                succeeded = true;
                return [value];
            }
            finally
            {
                if (!succeeded)
                    ReleaseUnpublished(value);
            }
        }

        private IReadOnlyList<CompatibilityRenderValue> MaterializeOpacityMask(
            RenderFragmentReference fragment,
            EffectiveScale? requestedScale)
        {
            if (fragment.Bounds.Width == 0 || fragment.Bounds.Height == 0)
            {
                foreach (RenderFragmentReference input in fragment.Inputs)
                    CompleteFragmentUse(input);
                MarkExecutionSkipped(fragment);
                return [];
            }

            EffectiveScale scale = requestedScale ?? ResolveConcreteScale(fragment, fragment.Bounds);
            CompatibilityRenderValue value = CreateOwnedValue(fragment.Bounds, scale);
            _diagnostics?.RecordGpuPassExecuted(fragment.Id?.Value ?? 0);
            bool succeeded = false;
            try
            {
                using var canvas = ImmediateCanvas.CreateExecutorManaged(
                    value.Target,
                    scale.Value,
                    _options.MaxWorkingScale,
                    value.RasterBounds.Size);
                using (canvas.PushTransform(Matrix.CreateTranslation(
                           -value.RasterBounds.X,
                           -value.RasterBounds.Y)))
                    ReplayOpacityMask(fragment, canvas);
                succeeded = true;
                return [value];
            }
            finally
            {
                if (!succeeded)
                    ReleaseUnpublished(value);
            }
        }

        private IReadOnlyList<CompatibilityRenderValue> ExecuteOpaque(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
        {
            var payload = (OpaqueRenderFragmentPayload)fragment.Payload!;
            OpaqueRenderDescription description = payload.Description;
            var flattened = new List<CompatibilityRenderValue>();
            EffectiveScale outputSupply = requestedScale
                ?? (!fragment.EffectiveScale.IsUnbounded
                    ? fragment.EffectiveScale
                    : EffectiveScale.At(currentTarget.Density));
            foreach (RenderFragmentReference input in fragment.Inputs)
            {
                flattened.AddRange(Materialize(
                    input,
                    currentTarget,
                    input.EffectiveScale.IsUnbounded ? outputSupply : null));
            }

            try
            {
                if (payload.Topology == OpaqueRenderTopology.Map)
                {
                    var mapped = new List<CompatibilityRenderValue>();
                    bool mapCallbackInvoked = false;
                    foreach (CompatibilityRenderValue input in flattened)
                    {
                        Rect outputBounds = description.Bounds.TransformBounds([input.CompleteBounds]);
                        EffectiveScale outputScale = requestedScale
                            ?? description.Scale.Resolve(
                                [input.EffectiveScale],
                                outputBounds,
                                _options.OutputScale,
                                _options.MaxWorkingScale);
                        mapped.AddRange(InvokeOpaque(
                            fragment,
                            description,
                            [input],
                            outputBounds,
                            outputScale,
                            description.ValueCardinality,
                            out bool currentCallbackInvoked));
                        mapCallbackInvoked |= currentCallbackInvoked;
                    }

                    if (!mapCallbackInvoked)
                        MarkExecutionSkipped(fragment);
                    return mapped;
                }

                Rect declaredBounds = description.Bounds.TransformBounds(
                    flattened.Select(static value => value.CompleteBounds).ToArray());
                EffectiveScale declaredScale = requestedScale
                    ?? description.Scale.Resolve(
                        flattened.Select(static value => value.EffectiveScale).ToArray(),
                        declaredBounds,
                        _options.OutputScale,
                        _options.MaxWorkingScale);
                IReadOnlyList<CompatibilityRenderValue> result = InvokeOpaque(
                    fragment,
                    description,
                    flattened,
                    declaredBounds,
                    declaredScale,
                    description.ValueCardinality,
                    out bool singleCallbackInvoked);
                if (!singleCallbackInvoked)
                    MarkExecutionSkipped(fragment);
                return result;
            }
            finally
            {
                foreach (RenderFragmentReference input in fragment.Inputs)
                    CompleteFragmentUse(input);
            }
        }

        private IReadOnlyList<CompatibilityRenderValue> ExecuteLegacyFilter(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget)
        {
            var inputs = new List<CompatibilityRenderValue>();
            EffectiveScale inputRequestScale = fragment.EffectiveScale.IsUnbounded
                ? EffectiveScale.At(currentTarget.Density)
                : fragment.EffectiveScale;
            foreach (RenderFragmentReference input in fragment.Inputs)
            {
                inputs.AddRange(Materialize(
                    input,
                    currentTarget,
                    input.EffectiveScale.IsUnbounded ? inputRequestScale : null));
            }

            var payload = (LegacyFilterEffectRenderFragmentPayload)fragment.Payload!;
            try
            {
                return payload.Context.Registry.Use(
                    payload.Context,
                    effectContext =>
                    {
                        _diagnostics?.RecordOpaqueExecution(fragment.Id?.Value ?? 0);
                        using var targets = new EffectTargets();
                        foreach (CompatibilityRenderValue input in inputs)
                        {
                            targets.Add(new EffectTarget(
                                input.Target,
                                input.Bounds,
                                input.EffectiveScale,
                                input.DeviceBounds)
                            {
                                OriginalBounds = new Rect(default, input.Bounds.Size),
                                Bounds = input.Bounds,
                            });
                        }

                        using var builder = new SKImageFilterBuilder();
                        using var activator = new FilterEffectActivator(
                            targets,
                            builder,
                            _options.Intent,
                            _options.Purpose,
                            _options.OutputScale,
                            fragment.EffectiveScale.Value,
                            _options.MaxWorkingScale);
                        activator.Apply(effectContext);
                        activator.Flush(force: payload.WorkingScalePolicy.HasValue);

                        var result = new List<CompatibilityRenderValue>(activator.CurrentTargets.Count);
                        foreach (EffectTarget target in activator.CurrentTargets)
                        {
                            if (target.RenderTarget is not { } renderTarget)
                                continue;

                            CompatibilityRenderValue value = MaterializeLegacyTarget(
                                target,
                                renderTarget,
                                fragment.Bounds);
                            _ownedValues.Add(value);

                            Rect selectedBounds = value.Bounds.Intersect(fragment.Bounds);
                            if (selectedBounds.Width == 0 || selectedBounds.Height == 0)
                            {
                                ReleaseUnpublished(value);
                                continue;
                            }

                            if (selectedBounds != value.Bounds)
                            {
                                CompatibilityRenderValue cropped = CropValue(value, selectedBounds);
                                ReleaseUnpublished(value);
                                value = cropped;
                            }

                            result.Add(value);
                        }

                        return (IReadOnlyList<CompatibilityRenderValue>)result;
                    });
            }
            finally
            {
                foreach (RenderFragmentReference input in fragment.Inputs)
                    CompleteFragmentUse(input);
            }
        }

        private CompatibilityRenderValue MaterializeLegacyTarget(
            EffectTarget target,
            RenderTarget renderTarget,
            Rect completeBounds)
        {
            Rect canonicalRasterBounds = target.DeviceBounds.ToRect(target.Scale.Value);
            PixelRect semanticDeviceBounds = PixelRect.FromRect(target.Bounds, target.Scale.Value);
            if (target.RasterBounds == canonicalRasterBounds
                && Contains(target.DeviceBounds, semanticDeviceBounds))
            {
                return new CompatibilityRenderValue(
                    renderTarget.ShallowCopy(),
                    target.Bounds,
                    target.Scale,
                    target.DeviceBounds,
                    ownsTarget: true,
                    completeBounds: completeBounds);
            }

            Rect physicalBounds = target.RasterBounds.Union(target.Bounds);
            float density = RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(
                physicalBounds,
                target.Scale.Value);
            EffectiveScale normalizedScale = EffectiveScale.At(density);
            PixelRect normalizedDeviceBounds = PixelRect.FromRect(physicalBounds, density);
            CompatibilityRenderValue normalized = CreateOwnedValue(
                target.Bounds,
                normalizedScale,
                completeBounds,
                physicalDeviceBounds: normalizedDeviceBounds);
            bool succeeded = false;
            try
            {
                using var canvas = ImmediateCanvas.CreateExecutorManaged(
                    normalized.Target,
                    normalized.EffectiveScale.Value,
                    _options.MaxWorkingScale,
                    normalized.RasterBounds.Size);
                using (canvas.PushTransform(Matrix.CreateTranslation(
                           -normalized.RasterBounds.X,
                           -normalized.RasterBounds.Y)))
                {
                    canvas.DrawRenderTargetScaledWithoutFlush(renderTarget, target.RasterBounds);
                }

                succeeded = true;
                return normalized;
            }
            finally
            {
                if (!succeeded)
                    ReleaseUnpublished(normalized);
            }
        }

        private static bool Contains(PixelRect outer, PixelRect inner)
            => outer.X <= inner.X
               && outer.Y <= inner.Y
               && outer.Right >= inner.Right
               && outer.Bottom >= inner.Bottom;

        private IReadOnlyList<CompatibilityRenderValue> ExecuteShader(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
        {
            if (fragment.Inputs.Length != 1)
                throw new InvalidOperationException("A Shader fragment requires exactly one input stream.");

            var payload = (ShaderRenderFragmentPayload)fragment.Payload!;
            ShaderDescription description = payload.Description;
            EffectiveScale inputRequestScale = requestedScale
                ?? (!fragment.EffectiveScale.IsUnbounded
                    ? fragment.EffectiveScale
                    : EffectiveScale.At(currentTarget.Density));
            IReadOnlyList<CompatibilityRenderValue> inputs = Materialize(
                fragment.Inputs[0],
                currentTarget,
                fragment.Inputs[0].EffectiveScale.IsUnbounded ? inputRequestScale : null);
            var results = new List<CompatibilityRenderValue>(inputs.Count);
            bool executed = false;
            try
            {
                foreach (CompatibilityRenderValue input in inputs)
                {
                    Rect outputBounds = description.Bounds.TransformBounds(input.CompleteBounds);
                    if (outputBounds.Width == 0 || outputBounds.Height == 0)
                        continue;

                    Rect requiredRegion = ResolveFragmentRequirement(fragment, outputBounds);
                    if (requiredRegion.Width == 0 || requiredRegion.Height == 0)
                        continue;

                    float density = !fragment.EffectiveScale.IsUnbounded
                        ? fragment.EffectiveScale.Value
                        : inputRequestScale.Value;
                    density = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(outputBounds, density);
                    EffectiveScale outputScale = EffectiveScale.At(density);
                    CompatibilityRenderValue output = CreateOwnedValue(
                        requiredRegion,
                        outputScale,
                        outputBounds);
                    bool succeeded = false;
                    try
                    {
                        ExecuteShaderElement(
                            fragment.Id?.Value ?? 0,
                            description,
                            input,
                            output,
                            outputBounds,
                            requiredRegion);
                        executed = true;
                        results.Add(output);
                        succeeded = true;
                    }
                    finally
                    {
                        if (!succeeded)
                            ReleaseUnpublished(output);
                    }
                }

                if (!executed)
                    MarkExecutionSkipped(fragment);
                return results;
            }
            catch
            {
                foreach (CompatibilityRenderValue value in results)
                    ReleaseUnpublished(value);
                throw;
            }
            finally
            {
                CompleteFragmentUse(fragment.Inputs[0]);
            }
        }

        private IReadOnlyList<CompatibilityRenderValue> ExecuteCompiledShaderRun(
            CompiledShaderRun run,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
        {
            Rect outputBounds = run.Output.Bounds;
            if (outputBounds.Width == 0 || outputBounds.Height == 0)
            {
                CompleteFragmentUse(run.Input);
                MarkExecutionSkipped(run.Output);
                return [];
            }

            Rect requiredRegion = ResolveFragmentRequirement(run.Output, outputBounds);
            if (requiredRegion.Width == 0 || requiredRegion.Height == 0)
            {
                CompleteFragmentUse(run.Input);
                MarkExecutionSkipped(run.Output);
                return [];
            }

            EffectiveScale outputRequestScale = !run.Output.EffectiveScale.IsUnbounded
                ? run.Output.EffectiveScale
                : requestedScale ?? EffectiveScale.At(currentTarget.Density);
            EffectiveScale inputRequestScale = requestedScale ?? outputRequestScale;
            IReadOnlyList<CompatibilityRenderValue> inputs = Materialize(
                run.Input,
                currentTarget,
                run.Input.EffectiveScale.IsUnbounded ? inputRequestScale : null);
            if (inputs.Count == 0)
            {
                CompleteFragmentUse(run.Input);
                MarkExecutionSkipped(run.Output);
                return [];
            }
            if (inputs.Count != 1)
            {
                throw new InvalidOperationException(
                    "A compiled Shader run requires its declared single input to materialize exactly one value.");
            }

            CompatibilityRenderValue input = inputs[0];
            float density = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(
                outputBounds,
                outputRequestScale.Value);
            CompatibilityRenderValue output = CreateOwnedValue(
                requiredRegion,
                EffectiveScale.At(density),
                outputBounds);
            bool succeeded = false;
            try
            {
                ExecuteCompiledShaderRunElement(
                    run,
                    input,
                    output,
                    outputBounds,
                    requiredRegion);
                succeeded = true;
                return [output];
            }
            finally
            {
                if (!succeeded)
                    ReleaseUnpublished(output);
                CompleteFragmentUse(run.Input);
            }
        }

        private void ExecuteCompiledShaderRunElement(
            CompiledShaderRun run,
            CompatibilityRenderValue input,
            CompatibilityRenderValue output,
            Rect outputBounds,
            Rect requiredRegion)
            => ExecuteCompiledShaderRunProgram(
                run,
                input,
                outputBounds,
                requiredRegion,
                output.DeviceBounds,
                output.EffectiveScale.Value,
                shader =>
                {
                    using var paint = new SKPaint { Shader = shader };
                    using var canvas = ImmediateCanvas.CreateExecutorManaged(
                        output.Target,
                        output.EffectiveScale.Value,
                        _options.MaxWorkingScale,
                        output.RasterBounds.Size);
                    canvas.Clear();
                    using (canvas.PushDeviceSpace())
                    {
                        canvas.Canvas.DrawRect(
                            SKRect.Create(output.Target.Width, output.Target.Height),
                            paint);
                    }
                });

        private void ExecuteCompiledShaderRunProgram(
            CompiledShaderRun run,
            CompatibilityRenderValue input,
            Rect outputBounds,
            Rect requiredRegion,
            PixelRect outputDeviceBounds,
            float outputScale,
            Action<SKShader> draw)
        {
            using SKImage inputImage = input.Target.Value.Snapshot();
            ProgramCacheContextKey contextKey = CreateProgramContextKey(input.Target, run.Program.Budget);
            using ProgramCacheLease<CachedSkRuntimeEffect> lease = AcquireProgram(run, contextKey);
            using var uniforms = new SKRuntimeEffectUniforms(lease.Program.Effect);
            using var runtimeChildren = new SKRuntimeEffectChildren(lease.Program.Effect);
            var bindingToken = new RenderExecutionSessionToken();
            var children = new List<SKShader>();
            try
            {
                try
                {
                    SKShader inputShader = inputImage.ToShader(
                        SKShaderTileMode.Decal,
                        SKShaderTileMode.Decal,
                        CreateInputLocalMatrix(
                            outputScale,
                            input.EffectiveScale.Value,
                            outputDeviceBounds,
                            input.DeviceBounds));
                    children.Add(inputShader);
                    runtimeChildren[SkslSnippetMerger.SourceChildName] = inputShader;

                    var stagesByMergedIndex = new Dictionary<int, CompiledShaderStage>();
                    var contextsByMergedIndex = new Dictionary<int, ShaderExecutionContext>();
                    for (int index = 0; index < run.Program.Stages.Count; index++)
                    {
                        int mergedIndex = run.Program.Stages[index].StageIndex;
                        CompiledShaderStage stage = run.Stages[index];
                        stagesByMergedIndex.Add(mergedIndex, stage);
                        contextsByMergedIndex.Add(
                            mergedIndex,
                            CreateCompiledShaderStageContext(
                                run,
                                stage,
                                index,
                                bindingToken,
                                input,
                                outputBounds,
                                requiredRegion,
                                outputDeviceBounds,
                                outputScale));
                    }

                    foreach (SkslMergedBindingLayout layout in run.Program.Bindings)
                    {
                        CompiledShaderStage stage = stagesByMergedIndex[layout.StageIndex];
                        ShaderExecutionContext context = contextsByMergedIndex[layout.StageIndex];
                        ShaderDescription description = stage.Description;
                        if (layout.Kind == SkslBindingKind.Uniform)
                        {
                            ShaderUniformBinding binding = description.Uniforms[layout.BindingIndex];
                            SkslUniformDeclaration declaration = description.Source.Uniforms[binding.Name];
                            ShaderUniformValue value = binding.Bind(declaration, context);
                            SetUniform(uniforms, layout.MergedName, declaration, value);
                        }
                        else
                        {
                            ShaderResourceBinding binding = description.Resources[layout.BindingIndex];
                            SKShader child = binding.Bind(context);
                            children.Add(child);
                            runtimeChildren[layout.MergedName] = child;
                        }
                    }
                }
                catch
                {
                    RecordFailure(
                        RenderPipelineFailurePhase.Binding,
                        run.Output.Id?.Value);
                    throw;
                }
                finally
                {
                    bindingToken.Complete();
                }

                using SKShader shader = lease.Program.Effect.ToShader(uniforms, runtimeChildren);
                draw(shader);

                _shaderRunExecutions++;
                _shaderStageExecutions = checked(_shaderStageExecutions + run.Stages.Length);
                if (run.IsFused)
                    _fusedShaderRunExecutions++;
                if (lease.IsCacheHit)
                    _programCacheHits++;
                _diagnostics?.RecordGpuPassExecuted(run.Output.Id?.Value ?? 0);
            }
            finally
            {
                foreach (SKShader child in children.AsEnumerable().Reverse())
                    child.Dispose();
            }
        }

        private ShaderExecutionContext CreateCompiledShaderStageContext(
            CompiledShaderRun run,
            CompiledShaderStage stage,
            int stageIndex,
            RenderExecutionSessionToken bindingToken,
            CompatibilityRenderValue runInput,
            Rect runOutputBounds,
            Rect runRequiredRegion,
            PixelRect runOutputDeviceBounds,
            float runWorkingScale)
        {
            bool isFirst = stageIndex == 0;
            bool isLast = stageIndex == run.Stages.Length - 1;
            RenderFragmentReference fragment = stage.Fragment;
            RenderFragmentReference fragmentInput = fragment.Inputs.Single();
            Rect inputBounds = isFirst ? runInput.Bounds : fragmentInput.Bounds;
            Rect outputBounds = isLast ? runOutputBounds : fragment.Bounds;
            Rect requiredRegion = isLast
                ? runRequiredRegion
                : ResolveFragmentRequirement(fragment, fragment.Bounds);
            EffectiveScale inputEffectiveScale = isFirst
                ? runInput.EffectiveScale
                : EffectiveScale.At(runWorkingScale);
            float workingScale = runWorkingScale;
            PixelRect deviceBounds = isLast
                ? runOutputDeviceBounds
                : PixelRect.FromRect(requiredRegion, workingScale);
            return new ShaderExecutionContext(
                bindingToken,
                inputBounds,
                outputBounds,
                requiredRegion,
                deviceBounds,
                inputEffectiveScale,
                _options.OutputScale,
                workingScale,
                _options.MaxWorkingScale,
                _options.Intent,
                _options.Purpose);
        }

        private ProgramCacheLease<CachedSkRuntimeEffect> AcquireProgram(
            CompiledShaderRun run,
            ProgramCacheContextKey contextKey)
        {
            try
            {
                ProgramCacheLease<CachedSkRuntimeEffect> lease = _programCache.GetOrCreate(
                    run.Program,
                    contextKey,
                    CachedSkRuntimeEffect.Create);
                _diagnostics?.RecordProgramCacheDecision(
                    run.Output.Id?.Value ?? 0,
                    lease.IsCacheHit);
                return lease;
            }
            catch
            {
                RecordFailure(
                    RenderPipelineFailurePhase.ProgramCompilation,
                    run.Output.Id?.Value);
                throw;
            }
        }

        private static ProgramCacheContextKey CreateProgramContextKey(
            RenderTarget target,
            SkslBackendBudget budget)
        {
            GRRecordingContext? context = target.Value.Context;
            object contextIdentity = context is null ? s_cpuProgramContext : context.Handle;
            object deviceIdentity = context is null ? s_cpuProgramDevice : context.Handle;
            return new ProgramCacheContextKey(
                deviceIdentity,
                contextIdentity,
                budget.CapabilityClass,
                "linear-premultiplied-rgba16f",
                s_defaultCompileOptions);
        }

        private static void SetUniform(
            SKRuntimeEffectUniforms uniforms,
            string name,
            SkslUniformDeclaration declaration,
            ShaderUniformValue value)
        {
            if (value.IsInteger)
            {
                uniforms[name] = declaration.ArrayExtent is null
                    && declaration.Type is "int" or "bool"
                        ? value.Integers![0]
                        : value.Integers!;
            }
            else
            {
                uniforms[name] = declaration.ArrayExtent is null
                    && declaration.Type is "float" or "half"
                        ? value.Floats![0]
                        : value.Floats!;
            }
        }

        private void ExecuteShaderElement(
            long subjectId,
            ShaderDescription description,
            CompatibilityRenderValue input,
            CompatibilityRenderValue output,
            Rect outputBounds,
            Rect requiredRegion)
        {
            using SKImage inputImage = input.Target.Value.Snapshot();
            string childName;
            string programSource;
            SKShaderTileMode tileMode;
            if (description.Kind == ShaderDescriptionKind.CurrentPixel)
            {
                childName = "__beutl_src";
                tileMode = SKShaderTileMode.Decal;
                programSource = $"uniform shader {childName};\n{description.Source.Text}\n"
                    + $"half4 main(float2 __beutl_coord) {{ return apply({childName}.eval(__beutl_coord)); }}\n";
            }
            else
            {
                childName = "src";
                tileMode = description.SourceTileMode;
                programSource = description.Source.Text;
            }

            using SKRuntimeEffect effect = CreateRuntimeEffect(subjectId, programSource);

            using var builder = new SKRuntimeShaderBuilder(effect);
            var bindingToken = new RenderExecutionSessionToken();
            var context = new ShaderExecutionContext(
                bindingToken,
                input.Bounds,
                outputBounds,
                requiredRegion,
                output.DeviceBounds,
                input.EffectiveScale,
                _options.OutputScale,
                output.EffectiveScale.Value,
                _options.MaxWorkingScale,
                _options.Intent,
                _options.Purpose);
            var children = new List<SKShader>();
            try
            {
                try
                {
                    foreach (ShaderUniformBinding binding in description.Uniforms)
                    {
                        if (!description.Source.Uniforms.TryGetValue(binding.Name, out SkslUniformDeclaration declaration))
                            throw new InvalidOperationException($"Shader uniform '{binding.Name}' was not declared.");
                        ShaderUniformValue value = binding.Bind(declaration, context);
                        if (value.IsInteger)
                        {
                            builder.Uniforms[binding.Name] = declaration.ArrayExtent is null
                                && declaration.Type is "int" or "bool"
                                    ? value.Integers![0]
                                    : value.Integers!;
                        }
                        else
                        {
                            builder.Uniforms[binding.Name] = declaration.ArrayExtent is null
                                && declaration.Type is "float" or "half"
                                    ? value.Floats![0]
                                    : value.Floats!;
                        }
                    }

                    SKShader inputShader = inputImage.ToShader(
                        tileMode,
                        tileMode,
                        CreateInputLocalMatrix(
                            output.EffectiveScale.Value,
                            input.EffectiveScale.Value,
                            output.DeviceBounds,
                            input.DeviceBounds));
                    children.Add(inputShader);
                    builder.Children[childName] = inputShader;

                    foreach (ShaderResourceBinding binding in description.Resources)
                    {
                        SKShader child = binding.Bind(context);
                        children.Add(child);
                        builder.Children[binding.Name] = child;
                    }
                }
                catch
                {
                    RecordFailure(RenderPipelineFailurePhase.Binding, subjectId);
                    throw;
                }
                finally
                {
                    bindingToken.Complete();
                }

                using SKShader shader = builder.Build();
                using var paint = new SKPaint { Shader = shader };
                using var canvas = ImmediateCanvas.CreateExecutorManaged(
                    output.Target,
                    output.EffectiveScale.Value,
                    _options.MaxWorkingScale,
                    output.RasterBounds.Size);
                canvas.Clear();
                using (canvas.PushDeviceSpace())
                {
                    canvas.Canvas.DrawRect(
                        SKRect.Create(output.Target.Width, output.Target.Height),
                        paint);
                }
                _diagnostics?.RecordGpuPassExecuted(subjectId);
            }
            finally
            {
                foreach (SKShader child in children.AsEnumerable().Reverse())
                    child.Dispose();
            }
        }

        private SKRuntimeEffect CreateRuntimeEffect(long subjectId, string programSource)
        {
            try
            {
                SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(programSource, out string? errorText);
                if (effect is null || !string.IsNullOrWhiteSpace(errorText))
                {
                    effect?.Dispose();
                    throw new InvalidOperationException(
                        $"SkSL program validation failed: {errorText ?? "the backend returned no program"}");
                }

                _diagnostics?.RecordProgramCacheDecision(subjectId, cacheHit: false);
                return effect;
            }
            catch
            {
                RecordFailure(RenderPipelineFailurePhase.ProgramCompilation, subjectId);
                throw;
            }
        }

        private IReadOnlyList<CompatibilityRenderValue> ExecuteGeometry(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget)
        {
            if (fragment.Inputs.Length != 1)
                throw new InvalidOperationException("A Geometry fragment requires exactly one input stream.");

            GeometryDescription description = ((GeometryRenderFragmentPayload)fragment.Payload!).Description;
            EffectiveScale requestScale = fragment.EffectiveScale.IsUnbounded
                ? EffectiveScale.At(currentTarget.Density)
                : fragment.EffectiveScale;
            IReadOnlyList<CompatibilityRenderValue> inputs = Materialize(
                fragment.Inputs[0],
                currentTarget,
                fragment.Inputs[0].EffectiveScale.IsUnbounded ? requestScale : null);
            var results = new List<CompatibilityRenderValue>(inputs.Count);
            bool executed = false;
            try
            {
                foreach (CompatibilityRenderValue input in inputs)
                {
                    Rect outputBounds = description.Bounds.TransformBounds(input.CompleteBounds);
                    if (outputBounds.Width == 0 || outputBounds.Height == 0)
                        continue;

                    Rect requiredRegion = ResolveFragmentRequirement(fragment, outputBounds);
                    if (requiredRegion.Width == 0 || requiredRegion.Height == 0)
                        continue;

                    float density = requestScale.Value;
                    density = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(outputBounds, density);
                    EffectiveScale outputScale = EffectiveScale.At(density);
                    CompatibilityRenderValue output = CreateOwnedValue(
                        requiredRegion,
                        outputScale,
                        outputBounds);
                    _diagnostics?.RecordGpuPassExecuted(fragment.Id?.Value ?? 0);
                    bool keepOutput = false;
                    try
                    {
                        Rect? finalBounds = ExecuteGeometryElement(
                            fragment,
                            description,
                            input,
                            output,
                            outputBounds,
                            requiredRegion);
                        executed = true;
                        if (finalBounds is not { Width: > 0, Height: > 0 } selectedBounds)
                            continue;

                        if (selectedBounds != requiredRegion)
                        {
                            CompatibilityRenderValue cropped = CropValue(output, selectedBounds);
                            ReleaseUnpublished(output);
                            output = cropped;
                        }

                        results.Add(output);
                        keepOutput = true;
                    }
                    finally
                    {
                        if (!keepOutput)
                            ReleaseUnpublished(output);
                    }
                }

                if (!executed)
                    MarkExecutionSkipped(fragment);
                return results;
            }
            catch
            {
                foreach (CompatibilityRenderValue value in results)
                    ReleaseUnpublished(value);
                throw;
            }
            finally
            {
                CompleteFragmentUse(fragment.Inputs[0]);
            }
        }

        private Rect? ExecuteGeometryElement(
            RenderFragmentReference fragment,
            GeometryDescription description,
            CompatibilityRenderValue input,
            CompatibilityRenderValue output,
            Rect outputBounds,
            Rect requiredRegion)
        {
            var token = new RenderExecutionSessionToken();
            using SKImage inputImage = input.Target.Value.Snapshot();
            try
            {
                Func<Bitmap>? createSnapshot = description.RequiresReadback
                    ? () => SnapshotInputForReadback(fragment, input)
                    : null;
                var executionInput = new RenderExecutionInput(
                    token,
                    input.Bounds,
                    input.EffectiveScale,
                    input.DeviceBounds,
                    inputImage,
                    createSnapshot,
                    description.RequiresReadback);
                var callbackCanvas = new RenderCallbackCanvas(
                    token,
                    output.EffectiveScale.Value,
                    requiredRegion,
                    output.DeviceBounds,
                    () => ImmediateCanvas.CreateExecutorManaged(
                        output.Target,
                        output.EffectiveScale.Value,
                        _options.MaxWorkingScale,
                        output.RasterBounds.Size),
                    CallbackCanvasCapability.Draw);
                var session = new GeometrySession(
                    token,
                    executionInput,
                    outputBounds,
                    requiredRegion,
                    output.DeviceBounds,
                    _options.OutputScale,
                    output.EffectiveScale.Value,
                    _options.MaxWorkingScale,
                    _options.Intent,
                    _options.Purpose,
                    callbackCanvas,
                    description.Resources);
                description.Render(session);
                if (session.IsOutputDiscarded)
                    return null;

                return session.OutputBounds.Intersect(requiredRegion);
            }
            finally
            {
                token.Complete();
            }
        }

        private CompatibilityRenderValue CropValue(
            CompatibilityRenderValue source,
            Rect selectedBounds)
        {
            CompatibilityRenderValue cropped = CreateOwnedValue(
                selectedBounds,
                source.EffectiveScale,
                source.CompleteBounds);
            bool succeeded = false;
            try
            {
                using var canvas = ImmediateCanvas.CreateExecutorManaged(
                    cropped.Target,
                    cropped.EffectiveScale.Value,
                    _options.MaxWorkingScale,
                    cropped.RasterBounds.Size);
                using (canvas.PushTransform(Matrix.CreateTranslation(
                           -cropped.RasterBounds.X,
                           -cropped.RasterBounds.Y)))
                {
                    canvas.ClipRect(selectedBounds);
                    canvas.DrawRenderTargetScaledWithoutFlush(source.Target, source.RasterBounds);
                }
                succeeded = true;
                return cropped;
            }
            finally
            {
                if (!succeeded)
                    ReleaseUnpublished(cropped);
            }
        }

        private static SKMatrix CreateInputLocalMatrix(
            float outputScale,
            float inputScale,
            PixelRect outputDeviceBounds,
            PixelRect inputDeviceBounds)
        {
            float scale = outputScale / inputScale;
            float outputOriginX = outputDeviceBounds.X / outputScale;
            float outputOriginY = outputDeviceBounds.Y / outputScale;
            float inputOriginX = inputDeviceBounds.X / inputScale;
            float inputOriginY = inputDeviceBounds.Y / inputScale;
            float offsetX = (outputOriginX - inputOriginX) * inputScale;
            float offsetY = (outputOriginY - inputOriginY) * inputScale;
            return new SKMatrix(
                scale,
                0,
                -offsetX * scale,
                0,
                scale,
                -offsetY * scale,
                0,
                0,
                1);
        }

        private IReadOnlyList<CompatibilityRenderValue> InvokeOpaque(
            RenderFragmentReference fragment,
            OpaqueRenderDescription description,
            IReadOnlyList<CompatibilityRenderValue> inputs,
            Rect outputBounds,
            EffectiveScale declaredScale,
            RenderValueCardinality cardinality,
            out bool callbackInvoked)
        {
            callbackInvoked = false;
            Rect requiredRegion = ResolveFragmentRequirement(fragment, outputBounds);
            if (requiredRegion.Width == 0 || requiredRegion.Height == 0)
                return [];

            var token = new RenderExecutionSessionToken();
            var inputImages = new List<SKImage>();
            var executionInputs = new List<RenderExecutionInput>(inputs.Count);
            var outputLeases = new Dictionary<OpaqueRenderOutput, CompatibilityRenderValue>(
                ReferenceEqualityComparer.Instance);
            var published = new List<CompatibilityRenderValue>();
            bool succeeded = false;
            try
            {
                foreach (CompatibilityRenderValue input in inputs)
                {
                    SKImage image = input.Target.Value.Snapshot();
                    inputImages.Add(image);
                    Func<Bitmap>? createSnapshot = description.RequiresReadback
                        ? () => SnapshotInputForReadback(fragment, input)
                        : null;
                    executionInputs.Add(new RenderExecutionInput(
                        token,
                        input.Bounds,
                        input.EffectiveScale,
                        input.DeviceBounds,
                        image,
                        createSnapshot,
                        description.RequiresReadback));
                }

                float density = declaredScale.IsUnbounded
                    ? RenderScaleUtilities.ClampWorkingScaleToBufferBudget(
                        outputBounds,
                        RenderScaleUtilities.ResolveWorkingScale(
                            inputs.Select(static value => value.EffectiveScale).ToArray(),
                            _options.OutputScale,
                            _options.MaxWorkingScale))
                    : declaredScale.Value;
                bool preserveRasterApron = description.DirectReplay is not null
                                           && fragment.Kind == RenderFragmentKind.OpaqueSource;
                if (preserveRasterApron)
                {
                    density = RenderScaleUtilities.ClampWorkingScaleToRasterApronBudget(
                        outputBounds,
                        density);
                }
                EffectiveScale concreteScale = EffectiveScale.At(density);
                OpaqueRenderSession? session = null;
                session = new OpaqueRenderSession(
                    token,
                    executionInputs,
                    outputBounds,
                    requiredRegion,
                    PixelRect.FromRect(requiredRegion, density),
                    _options.OutputScale,
                    density,
                    _options.MaxWorkingScale,
                    _options.Intent,
                    _options.Purpose,
                    description.Resources,
                    (_, logicalBounds) =>
                    {
                        PixelRect? physicalDeviceBounds = preserveRasterApron
                            ? RenderScaleUtilities.AddRasterApron(
                                PixelRect.FromRect(logicalBounds, density))
                            : null;
                        CompatibilityRenderValue value = CreateOwnedValue(
                            logicalBounds,
                            concreteScale,
                            outputBounds,
                            physicalDeviceBounds);
                        _diagnostics?.RecordGpuPassExecuted(fragment.Id?.Value ?? 0);
                        var canvas = new RenderCallbackCanvas(
                            token,
                            density,
                            logicalBounds,
                            value.DeviceBounds,
                            () => ImmediateCanvas.CreateExecutorManaged(
                                value.Target,
                                density,
                                _options.MaxWorkingScale,
                                value.RasterBounds.Size),
                            CallbackCanvasCapability.Draw);
                        var output = new OpaqueRenderOutput(
                            token,
                            session!,
                            logicalBounds,
                            concreteScale,
                            canvas,
                            _ => ReleaseUnpublished(value));
                        outputLeases.Add(output, value);
                        return output;
                    },
                    output =>
                    {
                        CompatibilityRenderValue value = outputLeases[output];
                        if (value.Bounds != output.Bounds)
                        {
                            CompatibilityRenderValue cropped = CropValue(value, output.Bounds);
                            ReleaseUnpublished(value);
                            outputLeases[output] = cropped;
                            value = cropped;
                        }
                        published.Add(value);
                    });

                callbackInvoked = true;
                description.Execute(session);
                ValidateOutputCount(cardinality, published.Count);
                if (description.BackendBoundary != RenderBackendBoundary.None && published.Count != 0)
                {
                    RecordSynchronization(fragment);
                    _diagnostics?.RecordBackendTransitionExecuted(fragment.Id?.Value ?? 0);
                }
                succeeded = true;
                return published.ToArray();
            }
            finally
            {
                foreach (SKImage image in inputImages)
                    image.Dispose();

                foreach (CompatibilityRenderValue value in outputLeases.Values)
                {
                    if (!succeeded || !published.Contains(value, ReferenceEqualityComparer.Instance))
                        ReleaseUnpublished(value);
                }

                token.Complete();
            }
        }

        private IReadOnlyList<CompatibilityRenderValue> MaterializeExternal(
            RenderFragmentReference fragment)
        {
            var payload = (MaterializedInputRenderFragmentPayload)fragment.Payload!;
            MaterializedInputDescription description = payload.Description;
            CompatibilityRenderValue value = description.Target.Registry.Use(
                description.Target,
                target =>
                {
                    description.ValidateTargetDeviceSize(target);
                    return new CompatibilityRenderValue(
                        target.ShallowCopy(),
                        description.Bounds,
                        description.EffectiveScale,
                        description.DeviceBounds,
                        ownsTarget: true);
                });
            _ownedValues.Add(value);
            return [value];
        }

        private void ExecuteTargetCommand(
            RenderFragmentReference fragment,
            ImmediateCanvas destination)
        {
            var payload = (TargetCommandRenderFragmentPayload)fragment.Payload!;
            TargetCommandDescription description = payload.Description;
            var values = new List<CompatibilityRenderValue>();
            foreach (RenderFragmentReference input in fragment.Inputs)
                values.AddRange(Materialize(input, destination));

            var token = new RenderExecutionSessionToken();
            var images = new List<SKImage>(values.Count);
            Bitmap? targetSnapshot = null;
            try
            {
                IReadOnlyList<RenderExecutionInput> inputs = CreateExecutionInputs(
                    token,
                    values,
                    description.RequiresInputReadback,
                    fragment,
                    images);
                Rect affectedBounds = ResolveTargetRegion(
                    description.AffectedRegion,
                    fragment,
                    destination);
                Rect requiredRegion = ResolveTargetAccessRequirement(fragment, affectedBounds);
                if (description.Access == TargetAccess.Readback
                    && (requiredRegion.Width == 0 || requiredRegion.Height == 0))
                {
                    // A readback-only root has no pixel-writing output requirement, but its
                    // authored callback still consumes the immutable preceding target token.
                    requiredRegion = affectedBounds;
                }
                CallbackCanvasCapability capability = description.AffectedRegion.Kind switch
                {
                    TargetRegionKind.Empty => CallbackCanvasCapability.TargetCommandEmpty,
                    TargetRegionKind.Region => CallbackCanvasCapability.TargetCommandRegion,
                    TargetRegionKind.Full => CallbackCanvasCapability.TargetCommandFull,
                    _ => throw new InvalidOperationException("The target-command region is uninitialized."),
                };
                var callbackCanvas = new RenderCallbackCanvas(
                    token,
                    destination.Density,
                    requiredRegion,
                    destination.CreateExecutionView,
                    capability,
                    mapLogicalOrigin: false);
                var session = new TargetCommandSession(
                    token,
                    inputs,
                    affectedBounds,
                    requiredRegion,
                    _options.Intent,
                    _options.Purpose,
                    callbackCanvas,
                    description.Resources,
                    description.Access == TargetAccess.Readback,
                    description.Access == TargetAccess.Readback
                        ? () => TakeTargetSnapshot(ref targetSnapshot)
                        : null);
                if (description.Access == TargetAccess.Readback)
                {
                    RecordSynchronization(fragment);
                    targetSnapshot = SnapshotTarget(destination, requiredRegion);
                }
                using (ObserveGpuPass(fragment))
                {
                    description.Execute(session);
                    session.ValidateCompletion();
                }
            }
            finally
            {
                targetSnapshot?.Dispose();
                foreach (SKImage image in images)
                    image.Dispose();
                token.Complete();
                foreach (RenderFragmentReference input in fragment.Inputs)
                    CompleteFragmentUse(input);
            }
        }

        private void ExecuteRawTargetCommand(
            RenderFragmentReference fragment,
            ImmediateCanvas destination)
        {
            RawTargetCommandDescription description =
                ((RawTargetCommandRenderFragmentPayload)fragment.Payload!).Description;
            var token = new RenderExecutionSessionToken();
            ImmediateCanvas view = destination.CreateExecutionView();
            try
            {
                token.UseRawCanvas(
                    view,
                    canvas =>
                    {
                        _diagnostics?.RecordOpaqueExecution(fragment.Id?.Value ?? 0);
                        description.Execute(new RawTargetCommandSession(
                            token,
                            canvas,
                            _options.Intent,
                            _options.Purpose,
                            description.Resources));
                    });
            }
            finally
            {
                token.Complete();
            }
        }

        private void ExecuteTargetScope(
            RenderFragmentReference fragment,
            ImmediateCanvas destination)
        {
            TargetScopeDescription description =
                ((TargetScopeRenderFragmentPayload)fragment.Payload!).Description;
            RenderFragmentReference input = fragment.Inputs.Single();
            var token = new RenderExecutionSessionToken();
            try
            {
                Rect? parentDomain = fragment.Id is { } id
                    && _resolvedParentScopeDomains.TryGetValue(id, out Rect resolvedParent)
                        ? resolvedParent
                        : _options.TargetDomain;
                Rect callbackBounds = TargetWriteMetadataResolver.Resolve(fragment, parentDomain)
                    ?? fragment.Bounds;
                Rect requiredRegion = ResolveFragmentRequirement(fragment, callbackBounds);
                var callbackCanvas = new RenderCallbackCanvas(
                    token,
                    destination.Density,
                    requiredRegion,
                    destination.CreateExecutionView,
                    CallbackCanvasCapability.TargetScope,
                    mapLogicalOrigin: false);
                var session = new TargetScopeSession(
                    token,
                    fragment.Bounds,
                    requiredRegion,
                    _options.Intent,
                    _options.Purpose,
                    callbackCanvas,
                    description.Resources,
                    canvas => Replay(input, canvas));
                description.Execute(session);
                session.ValidateCompletion();
            }
            finally
            {
                token.Complete();
            }
        }

        private IReadOnlyList<CompatibilityRenderValue> MaterializeValueReplayMap(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
        {
            if (fragment.Inputs.Length != 1
                || !fragment.ValueCardinality.Equals(RenderValueCardinality.Single))
            {
                throw new InvalidOperationException(
                    "A value replay map requires exactly one single-value input stream.");
            }

            Rect requiredRegion = ResolveFragmentRequirement(fragment, fragment.Bounds);
            if (requiredRegion.Width == 0 || requiredRegion.Height == 0)
            {
                CompleteFragmentUse(fragment.Inputs[0]);
                MarkExecutionSkipped(fragment);
                return [];
            }

            float requestedDensity = requestedScale?.Value
                ?? (fragment.EffectiveScale.IsUnbounded
                    ? currentTarget.Density
                    : fragment.EffectiveScale.Value);
            float density = RenderScaleUtilities.ClampWorkingScaleToRasterApronBudget(
                requiredRegion,
                requestedDensity);
            EffectiveScale scale = EffectiveScale.At(density);
            PixelRect deviceBounds = RenderScaleUtilities.AddRasterApron(
                PixelRect.FromRect(requiredRegion, density));
            CompatibilityRenderValue output = CreateOwnedValue(
                requiredRegion,
                scale,
                fragment.Bounds,
                deviceBounds);
            _diagnostics?.RecordGpuPassExecuted(fragment.Id?.Value ?? 0);
            bool succeeded = false;
            try
            {
                using var canvas = ImmediateCanvas.CreateExecutorManaged(
                    output.Target,
                    density,
                    _options.MaxWorkingScale,
                    output.RasterBounds.Size);
                canvas.Clear();
                using (canvas.PushTransform(Matrix.CreateTranslation(
                           -output.RasterBounds.X,
                           -output.RasterBounds.Y)))
                {
                    ExecuteTargetScope(fragment, canvas);
                }

                succeeded = true;
                return [output];
            }
            finally
            {
                if (!succeeded)
                    ReleaseUnpublished(output);
            }
        }

        private void ExecuteRawTargetScope(
            RenderFragmentReference fragment,
            ImmediateCanvas destination)
        {
            RawTargetScopeDescription description =
                ((RawTargetScopeRenderFragmentPayload)fragment.Payload!).Description;
            RenderFragmentReference input = fragment.Inputs.Single();
            var token = new RenderExecutionSessionToken();
            ImmediateCanvas view = destination.CreateExecutionView();
            try
            {
                token.UseRawCanvas(
                    view,
                    canvas =>
                    {
                        _diagnostics?.RecordOpaqueExecution(fragment.Id?.Value ?? 0);
                        var session = new RawTargetScopeSession(
                            token,
                            canvas,
                            fragment.Bounds,
                            _options.Intent,
                            _options.Purpose,
                            description.Resources,
                            replayCanvas => Replay(input, replayCanvas));
                        description.Execute(session);
                        session.ValidateCompletion();
                    });
            }
            finally
            {
                token.Complete();
            }
        }

        private IReadOnlyList<RenderExecutionInput> CreateExecutionInputs(
            RenderExecutionSessionToken token,
            IReadOnlyList<CompatibilityRenderValue> values,
            bool requiresReadback,
            RenderFragmentReference? readbackOwner,
            List<SKImage> images)
        {
            var inputs = new List<RenderExecutionInput>(values.Count);
            foreach (CompatibilityRenderValue value in values)
            {
                SKImage image = value.Target.Value.Snapshot();
                images.Add(image);
                Func<Bitmap>? createSnapshot = requiresReadback
                    ? () => SnapshotInputForReadback(readbackOwner!, value)
                    : null;
                inputs.Add(new RenderExecutionInput(
                    token,
                    value.Bounds,
                    value.EffectiveScale,
                    value.DeviceBounds,
                    image,
                    createSnapshot,
                    requiresReadback));
            }

            return inputs;
        }

        private Bitmap SnapshotInputForReadback(
            RenderFragmentReference owner,
            CompatibilityRenderValue value)
        {
            RecordSynchronization(owner);
            return value.Target.Snapshot();
        }

        private Rect ResolveFragmentRequirement(
            RenderFragmentReference fragment,
            Rect completeBounds)
            => _regions.GetFragmentRequirement(fragment)
                .Resolve(completeBounds)
                .Intersect(completeBounds);

        private Rect ResolveTargetAccessRequirement(
            RenderFragmentReference fragment,
            Rect completeBounds)
            => _regions.GetTargetAccessRequirement(fragment)
                .Resolve(completeBounds)
                .Intersect(completeBounds);

        private Rect ResolveTargetRegion(
            TargetRegion region,
            RenderFragmentReference fragment,
            ImmediateCanvas destination)
        {
            return region.Kind switch
            {
                TargetRegionKind.Empty => Rect.Empty,
                TargetRegionKind.Region => region.Value,
                TargetRegionKind.Full
                    when fragment.Id is { } id
                         && _resolvedAccessDomains.TryGetValue(id, out Rect domain) => domain,
                TargetRegionKind.Full when _options.TargetDomain is { } domain => domain,
                TargetRegionKind.Full => new Rect(default, destination.LogicalSize),
                _ => throw new InvalidOperationException("The target region is uninitialized."),
            };
        }

        private static Bitmap TakeTargetSnapshot(ref Bitmap? snapshot)
        {
            Bitmap result = snapshot
                ?? throw new InvalidOperationException("The target snapshot was already consumed.");
            snapshot = null;
            return result;
        }

        private static Bitmap SnapshotTarget(
            ImmediateCanvas destination,
            Rect requiredRegion)
        {
            using RenderTarget target = RenderTarget.GetRenderTarget(destination);
            using Bitmap snapshot = target.Snapshot();
            PixelRect targetBounds = new(0, 0, snapshot.Width, snapshot.Height);
            PixelRect sourceRegion = PixelRect.FromRect(
                    requiredRegion.TransformToAABB(destination.Transform),
                    1)
                .Intersect(targetBounds);
            if (sourceRegion.Width == 0 || sourceRegion.Height == 0)
            {
                throw new InvalidOperationException(
                    "A target readback requirement must resolve to a non-empty region on the current target.");
            }

            return snapshot.ExtractSubset(sourceRegion);
        }

        private IReadOnlyList<CompatibilityRenderValue> MaterializeLayer(
            RenderFragmentReference fragment,
            EffectiveScale? requestedScale)
        {
            if (_values.TryGetValue(fragment, out IReadOnlyList<CompatibilityRenderValue>? existing))
                return existing;

            Rect domain = ((LayerRenderFragmentPayload)fragment.Payload!).Domain
                ?? fragment.Bounds;
            EffectiveScale scale = requestedScale ?? ResolveConcreteScale(fragment, domain);
            CompatibilityRenderValue value = CreateOwnedValue(domain, scale);
            _diagnostics?.RecordGpuPassExecuted(fragment.Id?.Value ?? 0);
            using (var canvas = ImmediateCanvas.CreateExecutorManaged(
                       value.Target,
                       scale.Value,
                       _options.MaxWorkingScale,
                       value.RasterBounds.Size))
            using (canvas.PushTransform(Matrix.CreateTranslation(
                       -value.RasterBounds.X,
                       -value.RasterBounds.Y)))
            {
                foreach (RenderFragmentReference input in fragment.Inputs)
                    Replay(input, canvas);
            }

            return [value];
        }

        private IReadOnlyList<CompatibilityRenderValue> CaptureTarget(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget)
        {
            TargetCaptureDescription description = fragment.Payload switch
            {
                TargetCaptureRenderFragmentPayload payload => payload.Description,
                BuiltInBackdropCaptureRenderFragmentPayload payload => payload.Description,
                _ => throw new InvalidOperationException("The target-capture payload is invalid."),
            };
            Rect bounds = fragment.Kind == RenderFragmentKind.BuiltInBackdropCapture
                ? fragment.Bounds
                : description.Bounds;
            EffectiveScale scale = fragment.Kind == RenderFragmentKind.BuiltInBackdropCapture
                ? EffectiveScale.At(currentTarget.Density)
                : ResolveConcreteScale(fragment, bounds);
            CompatibilityRenderValue value = CreateOwnedValue(bounds, scale);
            _diagnostics?.RecordGpuPassExecuted(fragment.Id?.Value ?? 0);
            using (var canvas = ImmediateCanvas.CreateExecutorManaged(
                       value.Target,
                       scale.Value,
                       _options.MaxWorkingScale,
                       value.RasterBounds.Size))
            using (canvas.PushTransform(Matrix.CreateTranslation(
                       -value.RasterBounds.X,
                       -value.RasterBounds.Y)))
            {
                using RenderTarget source = RenderTarget.GetRenderTarget(currentTarget);
                canvas.DrawRenderTargetScaledWithoutFlush(source, bounds);
            }

            return [value];
        }

        private void ReplayTargetLayerScope(
            RenderFragmentReference fragment,
            ImmediateCanvas destination)
        {
            TargetRegion region = ((TargetLayerScopeRenderFragmentPayload)fragment.Payload!).Region;
            Rect domain = region.Kind switch
            {
                TargetRegionKind.Empty => Rect.Empty,
                TargetRegionKind.Region => region.Value,
                TargetRegionKind.Full
                    when fragment.Id is { } id
                         && _resolvedScopeDomains.TryGetValue(id, out Rect resolved) => resolved,
                TargetRegionKind.Full when _options.TargetDomain is { } targetDomain => targetDomain,
                TargetRegionKind.Full => new Rect(default, destination.LogicalSize),
                _ => throw new InvalidOperationException("The target-layer region is uninitialized."),
            };
            if (domain.Width == 0 || domain.Height == 0)
            {
                foreach (RenderFragmentReference input in fragment.Inputs)
                    CompleteFragmentUse(input);
                MarkExecutionSkipped(fragment);
                return;
            }

            EffectiveScale scale = EffectiveScale.At(destination.Density);
            CompatibilityRenderValue value = CreateOwnedValue(domain, scale);
            _diagnostics?.RecordGpuPassExecuted(fragment.Id?.Value ?? 0);
            using (var canvas = ImmediateCanvas.CreateExecutorManaged(
                       value.Target,
                       scale.Value,
                       _options.MaxWorkingScale,
                       value.RasterBounds.Size))
            using (canvas.PushTransform(Matrix.CreateTranslation(
                       -value.RasterBounds.X,
                       -value.RasterBounds.Y)))
            {
                foreach (RenderFragmentReference input in fragment.Inputs)
                    Replay(input, canvas);
            }

            DrawValue(value, destination);
            ReleaseUnpublished(value);
        }

        private void AddValueReferences(IEnumerable<CompatibilityRenderValue> values)
        {
            foreach (CompatibilityRenderValue value in values)
            {
                _valueReferences.TryGetValue(value, out int references);
                _valueReferences[value] = checked(references + 1);
            }
        }

        private void ReleaseValueReference(CompatibilityRenderValue value)
        {
            if (!_valueReferences.TryGetValue(value, out int references) || references <= 0)
                throw new InvalidOperationException("A render value reference was released more than once.");

            if (references > 1)
            {
                _valueReferences[value] = references - 1;
                return;
            }

            _valueReferences.Remove(value);
            if (!_cacheCaptureValues.Contains(value))
                ReleaseUnpublished(value);
        }

        private void CompleteFragmentUse(RenderFragmentReference fragment)
        {
            if (!_resourceUses.CompleteUse(fragment))
                return;
            if (!_values.Remove(fragment, out IReadOnlyList<CompatibilityRenderValue>? values))
                return;

            foreach (CompatibilityRenderValue value in values)
                ReleaseValueReference(value);
        }

        private void MarkExecutionSkipped(RenderFragmentReference fragment)
        {
            if (fragment.Id is { } id)
                _skippedExecutionSubjects.Add(id);
        }

        private IDisposable? ObserveGpuPass(RenderFragmentReference fragment)
        {
            if (_diagnostics is not { } diagnostics)
                return null;

            long subjectId = fragment.Id?.Value ?? 0;
            return ImmediateCanvas.ObservePixelOperations(
                () => diagnostics.RecordGpuPassExecuted(subjectId));
        }

        private static void AddResolvedDomain(
            Dictionary<RenderFragmentId, Rect> domains,
            RenderFragmentId fragmentId,
            Rect domain)
        {
            if (domains.TryGetValue(fragmentId, out Rect existing) && existing != domain)
            {
                throw new InvalidOperationException(
                    "One target-effect fragment cannot execute in two different target domains.");
            }

            domains[fragmentId] = domain;
        }

        private CompatibilityRenderValue CreateOwnedValue(
            Rect bounds,
            EffectiveScale scale,
            Rect? completeBounds = null,
            PixelRect? physicalDeviceBounds = null)
        {
            if (scale.IsUnbounded)
                throw new InvalidOperationException("An allocated render value requires a concrete density.");
            PixelRect deviceBounds = physicalDeviceBounds
                ?? PixelRect.FromRect(bounds, scale.Value);
            PixelRect semanticDeviceBounds = PixelRect.FromRect(bounds, scale.Value);
            if (deviceBounds.Width <= 0
                || deviceBounds.Height <= 0
                || deviceBounds.X > semanticDeviceBounds.X
                || deviceBounds.Y > semanticDeviceBounds.Y
                || deviceBounds.Right < semanticDeviceBounds.Right
                || deviceBounds.Bottom < semanticDeviceBounds.Bottom)
            {
                throw new ArgumentException(
                    "An allocated render value's physical device bounds must contain its semantic bounds.",
                    nameof(physicalDeviceBounds));
            }
            RenderTargetLease lease;
            RenderTargetPoolStatistics beforeAcquire = _targets.PoolStatistics;
            try
            {
                lease = _targets.Acquire(deviceBounds.Size);
            }
            catch
            {
                RenderTargetPoolStatistics afterFailure = _targets.PoolStatistics;
                _diagnostics?.RecordPoolMissWithoutAcquisition(
                    afterFailure.Misses - beforeAcquire.Misses);
                RecordFailure(RenderPipelineFailurePhase.Allocation, ActiveSubjectId);
                throw;
            }
            _intermediateTargetAcquisitions++;
            _diagnostics?.RecordIntermediateAcquired(
                created: !lease.WasReused,
                poolHit: lease.WasReused);
            _diagnostics?.RecordMaterialization(fullFrame: _options.RequestedRegion is null);
            bool succeeded = false;
            try
            {
                lease.Target.Value.Canvas.Clear(SKColors.Transparent);
                var value = new CompatibilityRenderValue(
                    lease,
                    bounds,
                    scale,
                    deviceBounds,
                    completeBounds);
                _ownedValues.Add(value);
                _diagnosticIntermediates.Add(value);
                succeeded = true;
                return value;
            }
            catch
            {
                RecordFailure(RenderPipelineFailurePhase.Allocation, ActiveSubjectId);
                throw;
            }
            finally
            {
                if (!succeeded)
                {
                    lease.Dispose();
                    _diagnostics?.RecordIntermediateDischarged();
                }
            }
        }

        private void ReleaseUnpublished(CompatibilityRenderValue value)
        {
            if (_ownedValues.Remove(value))
                DisposeOwnedValue(value);
        }

        private void DisposeOwnedValue(CompatibilityRenderValue value)
        {
            try
            {
                value.Dispose();
            }
            finally
            {
                if (_diagnosticIntermediates.Remove(value))
                    _diagnostics?.RecordIntermediateDischarged();
            }
        }

        private EffectiveScale ResolveConcreteScale(
            RenderFragmentReference fragment,
            Rect bounds)
        {
            if (!fragment.EffectiveScale.IsUnbounded)
                return fragment.EffectiveScale;
            float scale = RenderScaleUtilities.ResolveWorkingScale(
                fragment.Inputs.Select(static input => input.EffectiveScale).ToArray(),
                _options.OutputScale,
                _options.MaxWorkingScale);
            scale = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(bounds, scale);
            return EffectiveScale.At(scale);
        }

        private static void DrawValues(
            IReadOnlyList<CompatibilityRenderValue> values,
            ImmediateCanvas destination)
        {
            foreach (CompatibilityRenderValue value in values)
                DrawValue(value, destination);
        }

        private static void DrawValue(
            CompatibilityRenderValue value,
            ImmediateCanvas destination)
        {
            if (!value.PreferPixelExactComposite
                || !destination.TryDrawRenderTargetPixelAlignedWithoutFlush(
                    value.Target,
                    value.RasterBounds,
                    value.EffectiveScale.Value))
            {
                destination.DrawRenderTargetScaledWithoutFlush(value.Target, value.RasterBounds);
            }
        }

        private static void ValidateOutputCount(
            RenderValueCardinality cardinality,
            int count)
        {
            if (count < cardinality.Minimum
                || (cardinality.Maximum is { } maximum && count > maximum))
            {
                throw new InvalidOperationException(
                    $"The deferred callback published {count} values outside its declared cardinality "
                    + $"[{cardinality.Minimum}, {cardinality.Maximum?.ToString() ?? "unbounded"}].");
            }
        }

        private sealed record PendingRenderCacheCapture(
            RenderCacheMissCapture Descriptor,
            IReadOnlyList<CompatibilityRenderValue> Values);

        private sealed class PendingBackdropPublication(
            IBuiltInBackdropCaptureSink sink,
            Bitmap bitmap,
            float density)
        {
            public IBuiltInBackdropCaptureSink Sink { get; } = sink;

            public Bitmap? Bitmap { get; set; } = bitmap;

            public float Density { get; } = density;
        }
    }

    private sealed class CompatibilityRenderValue : IDisposable
    {
        private readonly RenderTargetLease? _lease;

        public CompatibilityRenderValue(
            RenderTarget target,
            Rect bounds,
            EffectiveScale effectiveScale,
            PixelRect deviceBounds,
            bool ownsTarget,
            Rect? completeBounds = null)
        {
            ArgumentNullException.ThrowIfNull(target);
            ValidatePhysicalFootprint(target, bounds, effectiveScale, deviceBounds);
            Target = target;
            Bounds = bounds;
            CompleteBounds = completeBounds ?? bounds;
            EffectiveScale = effectiveScale;
            DeviceBounds = deviceBounds;
            OwnsTarget = ownsTarget;
        }

        public CompatibilityRenderValue(
            RenderTargetLease lease,
            Rect bounds,
            EffectiveScale effectiveScale,
            PixelRect deviceBounds,
            Rect? completeBounds = null)
        {
            ArgumentNullException.ThrowIfNull(lease);
            ValidatePhysicalFootprint(lease.Target, bounds, effectiveScale, deviceBounds);
            _lease = lease;
            Target = lease.Target;
            Bounds = bounds;
            CompleteBounds = completeBounds ?? bounds;
            EffectiveScale = effectiveScale;
            DeviceBounds = deviceBounds;
            OwnsTarget = true;
        }

        public RenderTarget Target { get; }

        public Rect Bounds { get; set; }

        public Rect CompleteBounds { get; }

        public EffectiveScale EffectiveScale { get; }

        public PixelRect DeviceBounds { get; }

        public Rect RasterBounds => DeviceBounds.ToRect(EffectiveScale.Value);

        public bool OwnsTarget { get; }

        public bool PreferPixelExactComposite { get; set; }

        public RenderTarget TransferToAcceptedCache()
        {
            if (_lease is null)
            {
                throw new InvalidOperationException(
                    "Only a renderer-owned pooled capture can transfer into a persistent node cache.");
            }

            return _lease.TransferToAcceptedCache();
        }

        public void Dispose()
        {
            if (_lease is not null)
                _lease.Dispose();
            else if (OwnsTarget)
                Target.Dispose();
        }

        private static void ValidatePhysicalFootprint(
            RenderTarget target,
            Rect bounds,
            EffectiveScale effectiveScale,
            PixelRect deviceBounds)
        {
            if (effectiveScale.IsUnbounded)
                throw new ArgumentException("A materialized value requires a concrete density.", nameof(effectiveScale));
            if (deviceBounds.Size != new PixelSize(target.Width, target.Height))
            {
                throw new ArgumentException(
                    "A materialized value's device bounds must match its backing target size.",
                    nameof(deviceBounds));
            }

            PixelRect semanticDeviceBounds = PixelRect.FromRect(bounds, effectiveScale.Value);
            if (deviceBounds.X > semanticDeviceBounds.X
                || deviceBounds.Y > semanticDeviceBounds.Y
                || deviceBounds.Right < semanticDeviceBounds.Right
                || deviceBounds.Bottom < semanticDeviceBounds.Bottom)
            {
                throw new ArgumentException(
                    "A materialized value's device bounds must contain its semantic bounds.",
                    nameof(deviceBounds));
            }
        }
    }
}
