using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderRequestExecutor
{
    private readonly RenderTargetLeaseSession _targets;
    private readonly ProgramCache<CachedSkRuntimeEffect>? _programCache;
    private readonly ProgramCache<GLSLFilterPipeline>? _spirvProgramCache;
    private readonly ShaderBackendPreference _shaderBackendPreference;
    private readonly Action<RenderFragmentKind>? _afterCaptureAllocation;

    public RenderExecutionStatistics Statistics { get; private set; }

    internal static Rect GetDirectFilterLayerBounds(
        Rect semanticInputBounds,
        Rect replayedInputBounds,
        Rect? materializedRasterBounds = null)
        => materializedRasterBounds ?? replayedInputBounds;

    public RenderRequestExecutor(
        RenderTargetLeaseSession targets,
        ProgramCache<CachedSkRuntimeEffect>? programCache = null,
        Action<RenderFragmentKind>? afterCaptureAllocation = null,
        ProgramCache<GLSLFilterPipeline>? spirvProgramCache = null,
        ShaderBackendPreference shaderBackendPreference = ShaderBackendPreference.Auto)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _programCache = programCache;
        _spirvProgramCache = spirvProgramCache;
        _afterCaptureAllocation = afterCaptureAllocation;
        if (!Enum.IsDefined(shaderBackendPreference))
            throw new ArgumentOutOfRangeException(nameof(shaderBackendPreference));
        _shaderBackendPreference = shaderBackendPreference;
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

        ProgramCache<CachedSkRuntimeEffect>? localProgramCache = _programCache is null
            ? SkRuntimeEffectProgramCache.Create()
            : null;
        ProgramCache<CachedSkRuntimeEffect> familyProgramCache = _programCache ?? localProgramCache!;
        ProgramCache<GLSLFilterPipeline>? localSpirvProgramCache = _spirvProgramCache is null
            ? SpirvShaderProgramCache.Create()
            : null;
        ProgramCache<GLSLFilterPipeline> familySpirvProgramCache =
            _spirvProgramCache ?? localSpirvProgramCache!;
        var frames = new List<RenderRequestExecutionState>();
        var cleanupFailures = new List<Exception>();
        ExceptionDispatchInfo? primaryFailure = null;
        int nestedRootAcquisitions = 0;
        bool nestedPreviewDropObserved = false;
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
                    familySpirvProgramCache,
                    frames,
                    cleanupFailures,
                    ref nestedRootAcquisitions,
                    ref nestedPreviewDropObserved);
            }
            catch (FamilyExecutionException ex)
            {
                primaryFailure = ex.Failure;
            }
            catch (Exception ex)
            {
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
                        AddCleanupFailure(cleanupFailures, failure);
                        if (!alreadyRecorded)
                            owner.RecordCleanupFailure(failure);
                    }

                    if (primaryFailure is null && firstExternalCleanupFailure is not null)
                    {
                        primaryFailure = ExceptionDispatchInfo.Capture(firstExternalCleanupFailure);
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
                    AppendCleanupFailures(cleanupFailures, ex);
                }
            }

            if (localSpirvProgramCache is not null)
            {
                try
                {
                    localSpirvProgramCache.Dispose();
                }
                catch (Exception ex)
                {
                    AppendCleanupFailures(cleanupFailures, ex);
                }
            }

            if (primaryFailure is null && cleanupFailures.Count != 0)
            {
                primaryFailure = ExceptionDispatchInfo.Capture(cleanupFailures[0]);
            }

            if (primaryFailure is not null)
                RejectNestedBindings(request);

            EnsureOwnerPrimary(owner, primaryFailure?.SourceException);
            int ownerCleanupStart = owner.CleanupFailures.Length;
            owner.Cleanup();
            ImmutableArray<Exception> ownerCleanupFailures = owner.CleanupFailures;
            for (int index = ownerCleanupStart; index < ownerCleanupFailures.Length; index++)
            {
                Exception failure = ownerCleanupFailures[index];
                cleanupFailures.Add(failure);
                if (primaryFailure is null)
                {
                    primaryFailure = ExceptionDispatchInfo.Capture(failure);
                }
            }

            try
            {
                _targets.ThrowIfCleanupFailed();
            }
            catch (Exception ex)
            {
                RecordCleanupFailure(cleanupFailures, ref primaryFailure, ex);
            }

            if (primaryFailure is null)
            {
                try
                {
                    foreach (RenderRequestExecutionState state in frames)
                        state.PublishBuiltInBackdropCaptures();
                }
                catch (Exception ex)
                {
                    primaryFailure = ExceptionDispatchInfo.Capture(ex);
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
                    }
                    if (publicationCleanupFailures.Count != 0)
                    {
                        primaryFailure = ExceptionDispatchInfo.Capture(publicationCleanupFailures[0]);
                    }
                }
                catch (FamilyCachePublicationException ex)
                {
                    foreach (Exception cleanupFailure in ex.CleanupFailures)
                        AppendCleanupFailures(cleanupFailures, cleanupFailure);
                    primaryFailure = ex.Failure;
                }
                catch (Exception ex)
                {
                    primaryFailure = ExceptionDispatchInfo.Capture(ex);
                }
            }

            foreach (RenderRequestExecutionState state in frames)
            {
                try
                {
                    state.RejectCacheCaptures();
                    state.RejectBuiltInBackdropCaptures();
                }
                catch (Exception ex)
                {
                    RecordCleanupFailure(cleanupFailures, ref primaryFailure, ex);
                }
            }

            Statistics = AggregateStatistics(frames, nestedRootAcquisitions);
        }
        finally
        {
            // Every state is explicitly drained above. This fallback only protects
            // future edits that introduce an unexpected coordinator exception.
            foreach (RenderRequestExecutionState state in frames)
            {
                try
                {
                    state.Dispose();
                }
                catch (Exception ex)
                {
                    RecordCleanupFailure(cleanupFailures, ref primaryFailure, ex);
                }
            }
        }

        if (primaryFailure is not null)
        {
            EnsureOwnerPrimary(request.Request.Options.Owner, primaryFailure.SourceException);
            RecordAdditionalFailures(request.Request.Options.Owner, cleanupFailures);
            FailFamily(request);
            primaryFailure.Throw();
        }

        CompleteFamily(request);
    }

    /// <summary>
    /// Records one teardown failure, and promotes it to the primary failure when nothing has failed yet.
    /// </summary>
    /// <remarks>
    /// An aggregate is recorded as the whole set of leaves it carries, but only one exception can be
    /// rethrown as the primary, so the promotion takes the first leaf while the record keeps them all.
    /// </remarks>
    private static void RecordCleanupFailure(
        ICollection<Exception> cleanupFailures,
        ref ExceptionDispatchInfo? primaryFailure,
        Exception exception)
    {
        AppendCleanupFailures(cleanupFailures, exception);
        primaryFailure ??= ExceptionDispatchInfo.Capture(
            exception is AggregateException aggregate
                ? aggregate.Flatten().InnerExceptions[0]
                : exception);
    }

    private sealed partial class RenderRequestExecutionState : IDisposable
    {
        private readonly RenderRequestOptions _options;
        private readonly RecordedRenderGraph _graph;
        private readonly ExecutionIslandPlan _executionPlan;
        private readonly ExecutionIslandExecutionLedger _executionLedger;
        private readonly RegionAnalysis _regions;
        private readonly ResourcePlanUseTracker _resourceUses;
        private readonly RenderCacheResolution _cacheResolution;
        private readonly IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> _materializationDemands;
        private readonly IReadOnlySet<RenderFragmentReference> _previewDropEligibleMaterializations;
        private readonly RenderTargetLeaseSession _targets;
        private readonly RenderCacheDeviceContextIdentity _programCacheContext;
        private readonly ProgramCache<CachedSkRuntimeEffect> _programCache;
        private readonly ProgramCache<GLSLFilterPipeline> _spirvProgramCache;
        private readonly ShaderBackendPreference _shaderBackendPreference;
        private readonly DrawableBrushMaterializer _drawableBrushMaterializer;
        private readonly Action<RenderFragmentKind>? _afterCaptureAllocation;
        private Dictionary<RenderFragmentId, Rect>? _resolvedExecutionDomains;
        private readonly Dictionary<RenderFragmentReference, IReadOnlyList<MaterializedRenderValue>> _values =
            new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<MaterializedRenderValue> _ownedValues =
            new(ReferenceEqualityComparer.Instance);
        private HashSet<MaterializedRenderValue>? _cacheCaptureValues;
        private readonly Dictionary<MaterializedRenderValue, int> _valueReferences =
            new(ReferenceEqualityComparer.Instance);
        private static readonly IReadOnlyList<MaterializedRenderValue> s_suppressedCacheCapture = [];
        private readonly IReadOnlyList<MaterializedRenderValue>?[] _cacheCaptures;
        private List<(IBuiltInBackdropCaptureSink Sink, MaterializedRenderValue Value)>? _backdropCaptures;
        private List<PendingBackdropPublication>? _pendingBackdropPublications;
        private List<ImmediateCanvas>? _backdropSources;
        private int _shaderRunExecutions;
        private int _shaderStageExecutions;
        private int _fusedShaderRunExecutions;
        private int _spirvShaderRunExecutions;
        private int _intermediateTargetAcquisitions;
        private int _programCacheHits;
        private int _synchronizations;
        private int _replayDepth;
        private bool _previewAllocationDropObserved;
        private Vector _activeDeviceGridOffset;
        private bool _deviceGridPhaseNormalized;

        public RenderRequestExecutionState(
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
            ProgramCache<GLSLFilterPipeline> spirvProgramCache,
            ShaderBackendPreference shaderBackendPreference,
            Action<RenderFragmentKind>? afterCaptureAllocation)
        {
            _options = options;
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _executionPlan = executionPlan;
            _executionLedger = executionPlan.CreateExecutionLedger(graph);
            _regions = regions;
            _resourceUses = ResourcePlanUseTracker.Create(roots, cacheResolution);
            _cacheResolution = cacheResolution;
            _materializationDemands = materializationDemands
                ?? throw new ArgumentNullException(nameof(materializationDemands));
            _previewDropEligibleMaterializations = previewDropEligibleMaterializations
                ?? throw new ArgumentNullException(nameof(previewDropEligibleMaterializations));
            _targets = targets;
            _programCacheContext = targets.CacheDeviceContextIdentity;
            _programCache = programCache;
            _spirvProgramCache = spirvProgramCache;
            _shaderBackendPreference = shaderBackendPreference;
            _drawableBrushMaterializer = MaterializeDrawableBrush;
            _afterCaptureAllocation = afterCaptureAllocation;
            _cacheCaptures = cacheResolution.MissCaptureCount == 0
                ? []
                : new IReadOnlyList<MaterializedRenderValue>?[cacheResolution.Decisions.Length];

            ImmutableArray<TargetScopePlan> scopes = targetDependencies.Scopes;
            for (int index = 0; index < scopes.Length; index++)
            {
                TargetScopePlan scope = scopes[index];
                if (scope.Id.Value != index + 1)
                {
                    throw new InvalidOperationException(
                        "Target-scope IDs must be dense and match their plan order.");
                }
                if (scope.OwnerFragmentId is not { } owner)
                    continue;

                RenderFragmentReference fragment = graph.GetFragment(owner);
                if (scope.ResolvedDomain is { } domain
                    && fragment.Payload is TargetLayerScopeRenderFragmentPayload
                    {
                        Region.Kind: TargetRegionKind.Full,
                    })
                {
                    AddResolvedDomain(ref _resolvedExecutionDomains, owner, domain);
                }
                if (fragment.Kind == RenderFragmentKind.TargetScope
                    && scope.ParentId is { } parentId
                    && GetScope(scopes, parentId).ResolvedDomain is { } parentDomain)
                {
                    AddResolvedDomain(ref _resolvedExecutionDomains, owner, parentDomain);
                }
            }

            foreach (TargetDependencyStep step in targetDependencies.Steps)
            {
                RenderFragmentReference fragment = graph.GetFragment(step.FragmentId);
                if (fragment.Payload is TargetCommandRenderFragmentPayload
                    {
                        Description.AffectedRegion.Kind: TargetRegionKind.Full,
                    }
                    && GetScope(scopes, step.ScopeId).ResolvedDomain is { } domain)
                {
                    AddResolvedDomain(ref _resolvedExecutionDomains, step.FragmentId, domain);
                }
            }
        }

        /// <summary>
        /// Records that a nested subtree this request depends on was dropped for want of a target, so the
        /// request's degraded output is never published into the render cache.
        /// </summary>
        public void MarkPreviewAllocationDropped() => _previewAllocationDropObserved = true;

        /// <summary>
        /// Whether anything this request rendered was dropped for want of a target, which makes its output
        /// incomplete and therefore unfit for anything that outlives the frame.
        /// </summary>
        /// <remarks>
        /// The lease session is consulted alongside this request's own observation because the paths that
        /// allocate their own surfaces - tile-brush intermediates, custom-effect targets, effect flush
        /// buffers - degrade without ever taking a lease, and a frame missing their pixels is exactly as
        /// unfit for a cache as one whose materialization failed.
        /// </remarks>
        public bool PreviewAllocationDropObserved
            => _previewAllocationDropObserved || _targets.ContentDropObserved;

        public void Replay(RenderFragmentReference fragment, ImmediateCanvas destination)
            => Replay(fragment, destination, ResolveCallerScale(destination));

        /// <summary>
        /// Replays <paramref name="fragment"/> onto <paramref name="destination"/> for a caller that asks
        /// <paramref name="callerScale"/> of it.
        /// </summary>
        /// <remarks>
        /// The caller scale is the density this replay demands of an unbounded fragment, expressed in the
        /// fragment's own logical space: the destination density at a request root or a fresh buffer, carried
        /// backwards through every <see cref="RenderScopeTransformSpace.InputLogical"/> scope on the way down
        /// the same way <see cref="RenderMaterializationDemandResolver"/> compiled the demand it is checked
        /// against. <see cref="ImmediateCanvas.Density"/> alone is the surface's density and does not see the
        /// transform such a scope pushed on top of it, so a scope that shrinks its input would ask more of it
        /// than the plan compiled and trip the materialization cross-check.
        /// </remarks>
        private void Replay(
            RenderFragmentReference fragment,
            ImmediateCanvas destination,
            EffectiveScale callerScale)
        {
            _replayDepth++;
            try
            {
                ReplayCore(fragment, destination, callerScale);
                CompleteFragmentUse(fragment);
            }
            catch (PreviewAllocationDropException) when (_replayDepth == 1)
            {
                _previewAllocationDropObserved = true;
                _executionLedger.AbandonActive();
                CompleteFragmentUse(fragment);
            }
            catch (PreviewAllocationDropException)
            {
                throw;
            }
            finally
            {
                _replayDepth--;
            }
        }

        /// <summary>
        /// The scale a replay onto <paramref name="destination"/> asks of an unbounded fragment when nothing
        /// between them remaps the input's logical space.
        /// </summary>
        private EffectiveScale ResolveCallerScale(ImmediateCanvas destination)
            => EffectiveScale.At(MathF.Min(
                destination.Density,
                RenderScaleUtilities.SanitizeMaxWorkingScale(_options.MaxWorkingScale)));

        /// <summary>
        /// Carries the scale a scope is replayed at back to the input it replays, in the input's own logical
        /// space, bounded by the request ceiling at this step exactly as the demand resolver bounds it.
        /// </summary>
        private EffectiveScale ResolveScopeInputCallerScale(RenderScaleContract scale, EffectiveScale callerScale)
            => BoundCallerScale(scale.MapOutputDemandToInput(callerScale).Value);

        /// <summary>
        /// Carries the scale an operation is materialized at back to one of its inputs through the operation's
        /// declared <see cref="RenderInputDemandContract"/>, the step the demand resolver takes for a Shader, a
        /// Geometry, or a many-input opaque operation.
        /// </summary>
        private EffectiveScale ResolveInputCallerScale(
            RenderInputDemandContract inputDemand,
            int inputIndex,
            EffectiveScale outputScale)
            => inputDemand.IsUnchanged
                ? outputScale
                : BoundCallerScale(inputDemand.Resolve(inputIndex, outputScale).Value);

        /// <summary>
        /// Carries the scale an opaque operation is materialized at back to one of its inputs: a map through its
        /// scale contract, a combine or expand through its per-input demand contract, a source has no inputs.
        /// </summary>
        private EffectiveScale ResolveOpaqueInputCallerScale(
            RenderFragmentReference fragment,
            OpaqueRenderDescription description,
            int inputIndex,
            EffectiveScale outputScale)
            => fragment.Kind switch
            {
                RenderFragmentKind.OpaqueMap => ResolveScopeInputCallerScale(description.Scale, outputScale),
                RenderFragmentKind.OpaqueCombine or RenderFragmentKind.OpaqueExpand
                    => ResolveInputCallerScale(description.InputDemand, inputIndex, outputScale),
                _ => outputScale,
            };

        /// <summary>
        /// Carries the scale a compiled shader run is materialized at back to the run's input, stage by stage
        /// from the output, restarting from any stage that already has a concrete scale of its own.
        /// </summary>
        private EffectiveScale ResolveShaderRunInputCallerScale(CompiledShaderRun run, EffectiveScale outputScale)
        {
            EffectiveScale scale = outputScale;
            for (int stageIndex = run.StageFragmentIndices.Length - 1; stageIndex >= 0; stageIndex--)
            {
                RenderFragmentReference stage = run.GetStage(_graph, stageIndex);
                if (!stage.EffectiveScale.IsUnbounded)
                    scale = stage.EffectiveScale;
                scale = ResolveInputCallerScale(run.GetDescription(_graph, stageIndex).InputDemand, 0, scale);
            }

            return scale;
        }

        private EffectiveScale BoundCallerScale(float density)
            => EffectiveScale.At(MathF.Min(
                density,
                RenderScaleUtilities.SanitizeMaxWorkingScale(_options.MaxWorkingScale)));

        private void ReplayCore(
            RenderFragmentReference fragment,
            ImmediateCanvas destination,
            EffectiveScale callerScale)
        {
            if (fragment.Id is { } boundaryId
                && _cacheResolution.HasSelectedProducer(boundaryId))
            {
                IReadOnlyList<MaterializedRenderValue> boundaryValues = Materialize(
                    fragment,
                    destination,
                    fragment.EffectiveScale.IsUnbounded
                        ? callerScale
                        : null);
                if (fragment.ContributesValuesToTarget)
                    DrawValues(boundaryValues, destination);
                return;
            }

            bool canReplayMaterializedValue = fragment.Kind != RenderFragmentKind.BuiltInBackdropCapture;
            if (canReplayMaterializedValue
                && _values.TryGetValue(fragment, out IReadOnlyList<MaterializedRenderValue>? cachedValues))
            {
                if (fragment.ContributesValuesToTarget)
                    DrawValues(cachedValues, destination);
                return;
            }

            if (fragment.CanBeUsedAsValueInput
                && canReplayMaterializedValue
                && _resourceUses.GetRemainingUseCount(fragment) > 1)
            {
                DrawMaterializedFragment(fragment, destination, callerScale);
                return;
            }

            if (_executionPlan.TryGetMembership(_graph, fragment, out ExecutionIslandMembership membership)
                && membership.Island.ShaderRun is not null)
            {
                if (TryExecuteCompiledShaderRunDirect(
                        fragment,
                        membership.Island.ShaderRun,
                        destination,
                        callerScale))
                {
                    return;
                }
                DrawMaterializedFragment(fragment, destination, callerScale);
                return;
            }

            switch (fragment.Kind)
            {
                case RenderFragmentKind.ContributeValues:
                    // The values belong to the input, and this is the one replay branch that materializes
                    // without delegating to a method whose own finally completes the use. Leaving it open
                    // holds the input's pooled target for the rest of the request, so a chain of these keeps
                    // one live intermediate per link instead of handing each back as it is drawn.
                    try
                    {
                        DrawValues(
                            MaterializeSingleInput(
                                fragment,
                                destination,
                                fragment.EffectiveScale.IsUnbounded
                                    ? callerScale
                                    : null),
                            destination);
                    }
                    finally
                    {
                        CompleteFragmentUse(fragment.Inputs[0]);
                    }

                    return;
                case RenderFragmentKind.Opacity:
                    ExecuteReplayIsland(
                        fragment,
                        () =>
                        {
                            using (destination.PushOpacity(((OpacityRenderFragmentPayload)fragment.Payload!).Opacity))
                                Replay(fragment.Inputs.Single(), destination, callerScale);
                        });
                    return;
                case RenderFragmentKind.Blend:
                    ExecuteReplayIsland(
                        fragment,
                        () =>
                        {
                            BlendMode blendMode =
                                ((BlendRenderFragmentPayload)fragment.Payload!).BlendMode;
                            // DstOut leaves the destination unchanged for a transparent source,
                            // so a replay-safe source can erase directly without a coverage-changing
                            // intermediate layer. Other destructive modes still require the scope layer.
                            using (blendMode == BlendMode.DstOut
                                   && CanReplayWithDirectDstOut(fragment.Inputs.Single())
                                ? destination.PushDirectBlendMode(blendMode)
                                : destination.PushBlendMode(blendMode))
                            {
                                Replay(fragment.Inputs.Single(), destination, callerScale);
                            }
                        });
                    return;
                case RenderFragmentKind.OpacityMask:
                    ExecuteReplayIsland(fragment, () => ReplayOpacityMask(fragment, destination, callerScale));
                    return;
                case RenderFragmentKind.Layer:
                    if (fragment.ContributesValuesToTarget)
                        DrawValues(
                            Materialize(fragment, destination),
                            destination);
                    else
                        _ = Materialize(fragment, destination);
                    return;
                case RenderFragmentKind.TargetLayerScope:
                    ExecuteReplayIsland(fragment, () => ReplayTargetLayerScope(fragment, destination, callerScale));
                    return;
                case RenderFragmentKind.OpaqueSource:
                    if (TryReplayEngineSourceDirect(fragment, destination, callerScale))
                        return;
                    DrawMaterializedFragment(fragment, destination, callerScale);
                    return;
                case RenderFragmentKind.OpaqueMap:
                case RenderFragmentKind.OpaqueExpand:
                case RenderFragmentKind.MaterializedInput:
                case RenderFragmentKind.Shader:
                case RenderFragmentKind.Geometry:
                    DrawMaterializedFragment(fragment, destination, callerScale);
                    return;
                case RenderFragmentKind.FilterEffectSegment:
                    if (TryReplayBuiltInSkiaFilterChainDirect(fragment, destination, callerScale))
                        return;
                    DrawMaterializedFragment(fragment, destination, callerScale);
                    return;
                case RenderFragmentKind.OpaqueCombine:
                    if (TryReplayEngineSourceDirect(fragment, destination, callerScale))
                        return;
                    DrawMaterializedFragment(fragment, destination, callerScale);
                    return;
                case RenderFragmentKind.TargetCapture:
                    _ = Materialize(fragment, destination);
                    return;
                case RenderFragmentKind.BuiltInBackdropCapture:
                    {
                        IReadOnlyList<MaterializedRenderValue> values = Materialize(fragment, destination);
                        if (values.Count != 1
                            || ((BuiltInBackdropCaptureRenderFragmentPayload)fragment.Payload!).Identity
                            is not IBuiltInBackdropCaptureSink sink)
                        {
                            throw new InvalidOperationException(
                                "A built-in backdrop capture must produce one value for its publication sink.");
                        }

                        AddValueReferences(values);
                        (_backdropCaptures ??= []).Add((sink, values[0]));
                        return;
                    }
                case RenderFragmentKind.TargetCommand:
                    ExecuteReplayIsland(fragment, () => ExecuteTargetCommand(fragment, destination));
                    return;
                case RenderFragmentKind.RawTargetCommand:
                    ExecuteReplayIsland(fragment, () => ExecuteRawTargetCommand(fragment, destination));
                    return;
                case RenderFragmentKind.TargetScope:
                    ExecuteReplayIsland(fragment, () => ExecuteTargetScope(fragment, destination, callerScale));
                    return;
                case RenderFragmentKind.RawTargetScope:
                    ExecuteReplayIsland(fragment, () => ExecuteRawTargetScope(fragment, destination, callerScale));
                    return;
                default:
                    throw new InvalidOperationException("The recorded render-fragment kind is invalid.");
            }
        }

        /// <summary>Opens a canvas over one materialized value's buffer, sized and placed from the value.</summary>
        private ImmediateCanvas CreateValueCanvas(MaterializedRenderValue value)
            => CreateExecutorCanvas(
                value.Target,
                value.EffectiveScale.Value,
                _options.MaxWorkingScale,
                value.RasterBounds.Size,
                _options.Intent,
                value.DeviceBounds.Position);

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

    private sealed class MaterializedRenderValue : IDisposable
    {
        private readonly RenderTargetLease? _lease;
        private readonly EffectTargetRenderTargetLease? _effectTargetLease;

        public MaterializedRenderValue(
            RenderTarget target,
            Rect bounds,
            EffectiveScale effectiveScale,
            PixelRect deviceBounds,
            bool ownsTarget,
            Vector deviceGridOffset = default,
            Rect? completeBounds = null,
            bool preserveImperativeRasterPlacement = false)
        {
            ArgumentNullException.ThrowIfNull(target);
            ValidatePhysicalFootprint(
                target,
                bounds,
                effectiveScale,
                deviceBounds,
                deviceGridOffset,
                preserveImperativeRasterPlacement);
            Target = target;
            Bounds = bounds;
            CompleteBounds = completeBounds ?? bounds;
            EffectiveScale = effectiveScale;
            DeviceBounds = deviceBounds;
            DeviceGridOffset = deviceGridOffset;
            OwnsTarget = ownsTarget;
            PreserveImperativeRasterPlacement = preserveImperativeRasterPlacement;
        }

        public MaterializedRenderValue(
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

        public MaterializedRenderValue(
            EffectTargetRenderTargetLease lease,
            Rect bounds,
            EffectiveScale effectiveScale,
            PixelRect deviceBounds,
            Vector deviceGridOffset = default,
            Rect? completeBounds = null,
            bool preserveImperativeRasterPlacement = false)
        {
            ArgumentNullException.ThrowIfNull(lease);
            ValidatePhysicalFootprint(
                lease.Target,
                bounds,
                effectiveScale,
                deviceBounds,
                deviceGridOffset,
                preserveImperativeRasterPlacement);
            _effectTargetLease = lease;
            Target = lease.Target;
            Bounds = bounds;
            CompleteBounds = completeBounds ?? bounds;
            EffectiveScale = effectiveScale;
            DeviceBounds = deviceBounds;
            DeviceGridOffset = deviceGridOffset;
            OwnsTarget = true;
            PreserveImperativeRasterPlacement = preserveImperativeRasterPlacement;
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

        /// <summary>
        /// Gets the transform from this value's own coordinates to the buffer its device bounds were
        /// allocated against, carrying the device-grid phase the allocation was aligned to.
        /// </summary>
        public Matrix RasterAlignmentTransform
        {
            get
            {
                Vector translation = DeviceGridAlignment.ResolveRasterTranslation(
                    DeviceBounds,
                    DeviceGridOffset,
                    EffectiveScale.Value);
                return Matrix.CreateTranslation(translation.X, translation.Y);
            }
        }

        public bool OwnsTarget { get; }

        public bool PreserveImperativeRasterPlacement { get; }

        public RenderTarget TransferToAcceptedCache()
        {
            if (_effectTargetLease is not null)
                return _effectTargetLease.TransferToAcceptedCache();
            if (_lease is null)
            {
                throw new InvalidOperationException(
                    "Only a renderer-owned pooled capture can transfer into a persistent node cache.");
            }

            return _lease.TransferToAcceptedCache();
        }

        public void Dispose()
        {
            if (_effectTargetLease is not null)
                _effectTargetLease.Dispose();
            else if (_lease is not null)
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
            bool preserveImperativeRasterPlacement = false)
        {
            if (effectiveScale.IsUnbounded)
                throw new ArgumentException("A materialized value requires a concrete density.", nameof(effectiveScale));
            if (deviceBounds.Size != new PixelSize(target.Width, target.Height))
            {
                throw new ArgumentException(
                    "A materialized value's device bounds must match its backing target size.",
                    nameof(deviceBounds));
            }

            if (preserveImperativeRasterPlacement)
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
