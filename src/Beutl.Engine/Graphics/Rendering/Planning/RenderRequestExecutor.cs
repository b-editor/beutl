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

internal sealed partial class RenderRequestExecutor
{
    private readonly RenderTargetLeaseSession _targets;
    private readonly ProgramCache<CachedSkRuntimeEffect>? _programCache;
    private readonly Action<RenderFragmentKind>? _afterCaptureAllocation;

    public RenderExecutionStatistics Statistics { get; private set; }

    public RenderRequestExecutor(
        RenderTargetLeaseSession targets,
        ProgramCache<CachedSkRuntimeEffect>? programCache = null,
        Action<RenderFragmentKind>? afterCaptureAllocation = null)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _programCache = programCache;
        _afterCaptureAllocation = afterCaptureAllocation;
    }

    public void Execute(
        CompiledRenderRequest request,
        ImmediateCanvas destination,
        Action? finalizeOutput = null,
        Rect? replayBounds = null,
        Action? finalizeExternalResources = null)
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
        RenderRequestOwner owner = request.Request.Options.Owner;
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

            if (finalizeExternalResources is not null)
            {
                try
                {
                    finalizeExternalResources();
                }
                catch (Exception ex)
                {
                    EnsureOwnerPrimary(owner, primaryFailure?.SourceException);
                    IEnumerable<Exception> externalCleanupFailures = ex is AggregateException aggregate
                        ? aggregate.Flatten().InnerExceptions
                        : [ex];
                    Exception? firstExternalCleanupFailure = null;
                    foreach (Exception failure in externalCleanupFailures)
                    {
                        firstExternalCleanupFailure ??= failure;
                        bool alreadyRecorded = cleanupFailures.Any(
                            existing => ReferenceEquals(existing, failure));
                        AddCleanupFailure(cleanupFailures, rootDiagnostics, failure);
                        if (!alreadyRecorded)
                            owner.RecordCleanupFailure(failure);
                    }

                    if (primaryFailure is null && firstExternalCleanupFailure is not null)
                    {
                        primaryFailure = ExceptionDispatchInfo.Capture(firstExternalCleanupFailure);
                        failurePhase = RenderPipelineFailurePhase.Cleanup;
                    }
                }
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

    private sealed partial class CompatibilityExecutionState : IDisposable
    {
        private readonly RenderRequestOptions _options;
        private readonly ExecutionIslandPlan _executionPlan;
        private readonly ExecutionIslandExecutionLedger _executionLedger;
        private readonly RegionAnalysis _regions;
        private readonly ResourcePlanUseTracker _resourceUses;
        private readonly RenderCacheResolution _cacheResolution;
        private readonly IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> _materializationDemands;
        private readonly IReadOnlySet<RenderFragmentReference> _previewDropEligibleMaterializations;
        private readonly HashSet<RenderFragmentReference> _roots;
        private readonly RenderTargetLeaseSession _targets;
        private readonly RenderCacheDeviceContextIdentity _programCacheContext;
        private readonly ProgramCache<CachedSkRuntimeEffect> _programCache;
        private readonly RenderPipelineDiagnosticRecorder? _diagnostics;
        private readonly Action<RenderFragmentKind>? _afterCaptureAllocation;
        private readonly HashSet<ExecutionIslandId> _regionEmptyIslands;
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
        private readonly HashSet<RenderCacheCandidateId> _suppressedCacheCaptures = [];
        private readonly List<(IBuiltInBackdropCaptureSink Sink, CompatibilityRenderValue Value)> _backdropCaptures = [];
        private readonly List<PendingBackdropPublication> _pendingBackdropPublications = [];
        private int _shaderRunExecutions;
        private int _shaderStageExecutions;
        private int _fusedShaderRunExecutions;
        private int _intermediateTargetAcquisitions;
        private int _programCacheHits;
        private int _synchronizations;
        private int _replayDepth;
        private bool _previewAllocationDropObserved;
        private bool _verificationExecutionAbandoned;
        private Vector _activeDeviceGridOffset;

        public long? ActiveSubjectId { get; private set; }

        public RenderPipelineFailurePhase? FailurePhase { get; private set; }

        public CompatibilityExecutionState(
            RenderRequestOptions options,
            RecordedRenderGraph graph,
            ExecutionIslandPlan executionPlan,
            TargetDependencyPlan targetDependencies,
            RegionAnalysis regions,
            ImmutableArray<RenderFragmentReference> roots,
            IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> materializationDemands,
            IReadOnlySet<RenderFragmentReference> previewDropEligibleMaterializations,
            RenderCacheResolution cacheResolution,
            RenderTargetLeaseSession targets,
            ProgramCache<CachedSkRuntimeEffect> programCache,
            RenderPipelineDiagnosticRecorder? diagnostics,
            Action<RenderFragmentKind>? afterCaptureAllocation)
        {
            _options = options;
            _executionPlan = executionPlan;
            _executionLedger = executionPlan.CreateExecutionLedger(graph, roots, cacheResolution);
            _regions = regions;
            _regionEmptyIslands = executionPlan.Islands
                .Where(island => IsRegionEmpty(island, regions))
                .Select(static island => island.Id)
                .ToHashSet();
            HashSet<RenderFragmentId> cacheHitFragmentIds = cacheResolution.CollectPrunedHitProducers();
            _resourceUses = ResourcePlanUseSchedule.Create(roots, cacheHitFragmentIds).BeginExecution();
            _cacheResolution = cacheResolution;
            _materializationDemands = materializationDemands
                ?? throw new ArgumentNullException(nameof(materializationDemands));
            _previewDropEligibleMaterializations = previewDropEligibleMaterializations
                ?? throw new ArgumentNullException(nameof(previewDropEligibleMaterializations));
            _roots = new HashSet<RenderFragmentReference>(
                roots,
                ReferenceEqualityComparer.Instance);
            _targets = targets;
            _programCacheContext = targets.CacheDeviceContextIdentity;
            _programCache = programCache;
            _diagnostics = diagnostics;
            _afterCaptureAllocation = afterCaptureAllocation;
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

        public void Replay(RenderFragmentReference fragment, ImmediateCanvas destination)
        {
            _replayDepth++;
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
            catch (PreviewAllocationDropException) when (_replayDepth == 1)
            {
                _previewAllocationDropObserved = true;
                _executionLedger.AbandonActive();
                MarkExecutionSkipped(fragment);
                CompleteFragmentUse(fragment);
            }
            catch (PreviewAllocationDropException)
            {
                throw;
            }
            catch
            {
                RecordFailure(RenderPipelineFailurePhase.Execution, fragment.Id?.Value);
                throw;
            }
            finally
            {
                ActiveSubjectId = previous;
                _replayDepth--;
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
                            BlendMode blendMode =
                                ((BlendRenderFragmentPayload)fragment.Payload!).BlendMode;
                            using (ObserveGpuPass(fragment))
                            // DstOut leaves the destination unchanged for a transparent source,
                            // so a replay-safe source can erase directly without a coverage-changing
                            // intermediate layer. Other destructive modes still require the scope layer.
                            using (blendMode == BlendMode.DstOut
                                   && CanReplayWithDirectDstOut(fragment.Inputs.Single())
                                ? destination.PushDirectBlendMode(blendMode)
                                : destination.PushBlendMode(blendMode))
                            {
                                Replay(fragment.Inputs.Single(), destination);
                            }
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
            Vector deviceGridOffset = default,
            Rect? completeBounds = null,
            bool preserveLegacyRasterPlacement = false)
        {
            ArgumentNullException.ThrowIfNull(target);
            ValidatePhysicalFootprint(
                target,
                bounds,
                effectiveScale,
                deviceBounds,
                deviceGridOffset,
                preserveLegacyRasterPlacement);
            Target = target;
            Bounds = bounds;
            CompleteBounds = completeBounds ?? bounds;
            EffectiveScale = effectiveScale;
            DeviceBounds = deviceBounds;
            DeviceGridOffset = deviceGridOffset;
            OwnsTarget = ownsTarget;
            PreserveLegacyRasterPlacement = preserveLegacyRasterPlacement;
        }

        public CompatibilityRenderValue(
            RenderTargetLease lease,
            Rect bounds,
            EffectiveScale effectiveScale,
            PixelRect deviceBounds,
            Vector deviceGridOffset = default,
            Rect? completeBounds = null)
        {
            ArgumentNullException.ThrowIfNull(lease);
            ValidatePhysicalFootprint(
                lease.Target,
                bounds,
                effectiveScale,
                deviceBounds,
                deviceGridOffset);
            _lease = lease;
            Target = lease.Target;
            Bounds = bounds;
            CompleteBounds = completeBounds ?? bounds;
            EffectiveScale = effectiveScale;
            DeviceBounds = deviceBounds;
            DeviceGridOffset = deviceGridOffset;
            OwnsTarget = true;
        }

        public RenderTarget Target { get; }

        public Rect Bounds { get; set; }

        public Rect CompleteBounds { get; }

        public EffectiveScale EffectiveScale { get; }

        public PixelRect DeviceBounds { get; }

        public Vector DeviceGridOffset { get; }

        public Rect RasterBounds
            => DeviceBounds
                .ToRect(EffectiveScale.Value)
                .Translate(-DeviceGridOffset);

        public bool OwnsTarget { get; }

        public bool PreserveLegacyRasterPlacement { get; }

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
            PixelRect deviceBounds,
            Vector deviceGridOffset,
            bool preserveLegacyRasterPlacement = false)
        {
            if (effectiveScale.IsUnbounded)
                throw new ArgumentException("A materialized value requires a concrete density.", nameof(effectiveScale));
            if (deviceBounds.Size != new PixelSize(target.Width, target.Height))
            {
                throw new ArgumentException(
                    "A materialized value's device bounds must match its backing target size.",
                    nameof(deviceBounds));
            }

            if (preserveLegacyRasterPlacement)
                return;

            PixelRect semanticDeviceBounds = PixelRect.FromRect(
                bounds.Translate(deviceGridOffset),
                effectiveScale.Value);
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
