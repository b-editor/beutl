using System.Collections.Immutable;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

internal sealed class RenderRequestCompiler
{
    private readonly StructuralPlanCache? _structuralPlanCache;
    private readonly RenderCacheResolutionContext? _renderCacheContext;
    private readonly IRenderCacheLookup? _renderCacheLookup;

    public RenderRequestCompiler(
        StructuralPlanCache? structuralPlanCache = null,
        RenderCacheResolutionContext? renderCacheContext = null,
        IRenderCacheLookup? renderCacheLookup = null)
    {
        _structuralPlanCache = structuralPlanCache;
        _renderCacheContext = renderCacheContext;
        _renderCacheLookup = renderCacheLookup;
    }

    public RenderNodeMeasurement ResolveMetadata(
        RenderRequest request,
        RecordedRenderGraph graph)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(graph);
        var context = new FamilyCompilationContext();
        try
        {
            var measurements = new Dictionary<RenderRequest, RenderNodeMeasurement>(
                ReferenceEqualityComparer.Instance);
            ResolveMetadataFamily(request, graph, measurements, context);
            if (request.Options.Purpose is RenderRequestPurpose.Bounds or RenderRequestPurpose.HitTest)
                CompleteMetadataFamily(request, graph);
            return measurements[request];
        }
        catch (Exception ex)
        {
            FailFamily(request, graph, ex, context.FailurePhase);
            throw;
        }
    }

    public CompiledRenderRequest Compile(
        RenderRequest request,
        RecordedRenderGraph graph)
        => Compile(request, graph, SkslBackendBudget.Unlimited);

    internal CompiledRenderRequest Compile(
        RenderRequest request,
        RecordedRenderGraph graph,
        SkslBackendBudget shaderBudget)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(shaderBudget);
        var context = new FamilyCompilationContext();
        try
        {
            var measurements = new Dictionary<RenderRequest, RenderNodeMeasurement>(
                ReferenceEqualityComparer.Instance);
            ResolveMetadataFamily(request, graph, measurements, context);
            int nextStructuralPlanSlot = 0;
            CompiledRenderRequest compiled = CompileFamily(
                request,
                graph,
                measurements,
                shaderBudget,
                context,
                ref nextStructuralPlanSlot);
            _structuralPlanCache?.RetainFamilySlots(nextStructuralPlanSlot);
            return compiled;
        }
        catch (Exception ex)
        {
            FailFamily(request, graph, ex, context.FailurePhase);
            throw;
        }
    }

    public CompiledRenderRequest CompileAfterMetadata(
        RenderRequest request,
        RecordedRenderGraph graph,
        RenderNodeMeasurement measurement)
        => CompileAfterMetadata(
            request,
            graph,
            measurement,
            SkslBackendBudget.Unlimited);

    internal CompiledRenderRequest CompileAfterMetadata(
        RenderRequest request,
        RecordedRenderGraph graph,
        RenderNodeMeasurement measurement,
        SkslBackendBudget shaderBudget)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(shaderBudget);
        if (request.State != RenderRequestState.MetadataResolved)
        {
            throw new InvalidOperationException(
                "A render request can be compiled only after metadata resolution.");
        }

        var context = new FamilyCompilationContext();
        try
        {
            var measurements = new Dictionary<RenderRequest, RenderNodeMeasurement>(
                ReferenceEqualityComparer.Instance)
            {
                [request] = measurement,
            };
            CollectNestedMetadata(graph, measurements, context);
            int nextStructuralPlanSlot = 0;
            CompiledRenderRequest compiled = CompileFamily(
                request,
                graph,
                measurements,
                shaderBudget,
                context,
                ref nextStructuralPlanSlot);
            _structuralPlanCache?.RetainFamilySlots(nextStructuralPlanSlot);
            return compiled;
        }
        catch (Exception ex)
        {
            FailFamily(request, graph, ex, context.FailurePhase);
            throw;
        }
    }

    private void ResolveMetadataFamily(
        RenderRequest request,
        RecordedRenderGraph graph,
        IDictionary<RenderRequest, RenderNodeMeasurement> measurements,
        FamilyCompilationContext context)
    {
        foreach (RecordedNestedRenderRequest nested in graph.NestedRequests)
            ResolveMetadataFamily(nested.Request, nested.Graph, measurements, context);

        context.Set(request, RenderPipelineFailurePhase.Metadata);
        if (request.State != RenderRequestState.Recorded)
        {
            throw new InvalidOperationException(
                "Render-request metadata can be resolved only after recording completes.");
        }

        request.TransitionTo(RenderRequestState.TargetDependenciesLowered);
        ImmutableArray<RenderFragmentReference> roots = ResolveRoots(graph);
        TargetDependencyPlan targetDependencies = TargetDependencyLowerer.Lower(
            roots,
            request.Options.TargetDomain);
        context.Set(request, RenderPipelineFailurePhase.RegionAnalysis);
        RenderNodeMeasurement measurement = new RegionAnalyzer()
            .Analyze(request.Options, roots, targetDependencies)
            .Measurement;
        request.TransitionTo(RenderRequestState.MetadataResolved);
        measurements.Add(request, measurement);
    }

    private void CollectNestedMetadata(
        RecordedRenderGraph graph,
        IDictionary<RenderRequest, RenderNodeMeasurement> measurements,
        FamilyCompilationContext context)
    {
        foreach (RecordedNestedRenderRequest nested in graph.NestedRequests)
        {
            if (nested.Request.State == RenderRequestState.Recorded)
            {
                ResolveMetadataFamily(nested.Request, nested.Graph, measurements, context);
            }
            else if (nested.Request.State == RenderRequestState.MetadataResolved)
            {
                CollectNestedMetadata(nested.Graph, measurements, context);
                context.Set(nested.Request, RenderPipelineFailurePhase.RegionAnalysis);
                ImmutableArray<RenderFragmentReference> roots = ResolveRoots(nested.Graph);
                TargetDependencyPlan targetDependencies = TargetDependencyLowerer.Lower(
                    roots,
                    nested.Request.Options.TargetDomain);
                measurements[nested.Request] = new RegionAnalyzer()
                    .Analyze(nested.Request.Options, roots, targetDependencies)
                    .Measurement;
            }
            else
            {
                context.Set(nested.Request, RenderPipelineFailurePhase.Metadata);
                throw new InvalidOperationException(
                    "A nested render request must be recorded or metadata-resolved before family compilation.");
            }
        }
    }

    private CompiledRenderRequest CompileFamily(
        RenderRequest request,
        RecordedRenderGraph graph,
        IReadOnlyDictionary<RenderRequest, RenderNodeMeasurement> measurements,
        SkslBackendBudget shaderBudget,
        FamilyCompilationContext context,
        ref int nextStructuralPlanSlot)
    {
        var nested = ImmutableArray.CreateBuilder<CompiledRenderRequest>(graph.NestedRequests.Length);
        foreach (RecordedNestedRenderRequest recordedNested in graph.NestedRequests)
        {
            nested.Add(CompileFamily(
                recordedNested.Request,
                recordedNested.Graph,
                measurements,
                shaderBudget,
                context,
                ref nextStructuralPlanSlot));
        }

        int structuralPlanSlot = nextStructuralPlanSlot++;
        return CompileSingle(
            request,
            graph,
            measurements[request],
            shaderBudget,
            nested.MoveToImmutable(),
            context,
            structuralPlanSlot);
    }

    private CompiledRenderRequest CompileSingle(
        RenderRequest request,
        RecordedRenderGraph graph,
        RenderNodeMeasurement measurement,
        SkslBackendBudget shaderBudget,
        ImmutableArray<CompiledRenderRequest> nestedRequests,
        FamilyCompilationContext context,
        int structuralPlanSlot)
    {
        context.Set(request, RenderPipelineFailurePhase.RegionAnalysis);
        if (request.State != RenderRequestState.MetadataResolved)
        {
            throw new InvalidOperationException(
                "A render request can be compiled only after metadata resolution.");
        }

        RenderPipelineDiagnosticRecorder? diagnostics = RenderRequestDiagnostics.TryGet(request);
        ImmutableArray<RenderFragmentReference> roots = ResolveRoots(graph);
        TargetDependencyPlan targetDependencies = TargetDependencyLowerer.Lower(
            roots,
            request.Options.TargetDomain);
        RegionAnalysis regions = new RegionAnalyzer().Analyze(
            request.Options,
            roots,
            targetDependencies);
        if (regions.Measurement != measurement)
        {
            throw new InvalidOperationException(
                "The supplied metadata does not match graph-wide region analysis.");
        }

        request.TransitionTo(RenderRequestState.RegionsResolved);
        context.Set(request, RenderPipelineFailurePhase.CacheResolution);
        RenderCacheResolutionContext cacheContext = _renderCacheContext
            ?? new RenderCacheResolutionContext(
                RenderCacheFormatIdentity.LinearPremultipliedRgba16Float,
                new RenderCacheDeviceContextIdentity(request, request),
                allowPersistentLookup: false,
                allowCapturePublication: false);
        RenderCacheResolution cacheResolution = new RenderCacheResolver().Resolve(
            request,
            graph,
            regions,
            cacheContext,
            _renderCacheLookup);
        RecordCacheDecisions(diagnostics, cacheResolution);
        request.TransitionTo(RenderRequestState.CachesResolved);
        context.Set(request, RenderPipelineFailurePhase.Planning);
        ExecutionIslandPlan executionPlan;
        if (_structuralPlanCache is not null)
        {
            StructuralPlanCacheStatistics before = _structuralPlanCache.Statistics;
            StructuralPlanIdentity structuralIdentity = StructuralPlanIdentity.Create(
                request.Options.PlanIdentity,
                graph,
                shaderBudget,
                cacheResolution);
            executionPlan = _structuralPlanCache.GetOrCompile(
                structuralIdentity,
                graph,
                () => new ExecutionIslandPlanner().Plan(
                    graph,
                    roots,
                    cacheResolution,
                    request.Options.FusionMode,
                    shaderBudget),
                familySlot: structuralPlanSlot);
            StructuralPlanCacheStatistics after = _structuralPlanCache.Statistics;
            diagnostics?.RecordStructuralPlanDecision(
                cacheHit: after.Hits > before.Hits,
                compiled: after.Compilations > before.Compilations);
        }
        else
        {
            executionPlan = new ExecutionIslandPlanner().Plan(
                graph,
                roots,
                cacheResolution,
                request.Options.FusionMode,
                shaderBudget);
            diagnostics?.RecordStructuralPlanDecision(cacheHit: false, compiled: true);
        }

        diagnostics?.RecordPlan(executionPlan);
        request.TransitionTo(RenderRequestState.Planned);
        return new CompiledRenderRequest(
            request,
            graph,
            regions,
            roots,
            targetDependencies,
            cacheResolution,
            executionPlan,
            nestedRequests);
    }

    private static void RecordCacheDecisions(
        RenderPipelineDiagnosticRecorder? diagnostics,
        RenderCacheResolution resolution)
    {
        if (diagnostics is null)
            return;

        foreach (RenderCacheDecision decision in resolution.Decisions)
        {
            switch (decision.Kind)
            {
                case RenderCacheResolutionKind.Hit:
                    diagnostics.RecordCacheDecision(decision.Candidate.FragmentId.Value, cacheHit: true);
                    break;
                case RenderCacheResolutionKind.MissCapture:
                    diagnostics.RecordCacheDecision(decision.Candidate.FragmentId.Value, cacheHit: false);
                    break;
            }
        }
    }

    internal static ImmutableArray<RenderFragmentReference> ResolveRoots(
        RecordedRenderGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (graph.PublicationRoots.IsDefaultOrEmpty)
            return [];

        var byId = new Dictionary<RenderFragmentId, RenderFragmentReference>();
        foreach (RecordedRenderFragment fragment in graph.Fragments)
        {
            if (fragment.Payload is not RenderFragmentReference reference)
            {
                throw new InvalidOperationException(
                    "A recorded render fragment is missing its executable semantic reference.");
            }

            byId.Add(fragment.Id, reference);
        }

        var roots = ImmutableArray.CreateBuilder<RenderFragmentReference>(graph.PublicationRoots.Length);
        foreach (RenderFragmentId id in graph.PublicationRoots)
        {
            if (!byId.TryGetValue(id, out RenderFragmentReference? reference))
                throw new InvalidOperationException("A publication root does not identify a recorded fragment.");
            roots.Add(reference);
        }

        return roots.MoveToImmutable();
    }

    private static void CompleteMetadataFamily(RenderRequest root, RecordedRenderGraph graph)
    {
        foreach ((RenderRequest request, _) in EnumerateFamilyDepthFirst(root, graph))
        {
            RenderPipelineDiagnosticRecorder? diagnostics = RenderRequestDiagnostics.TryGet(request);
            diagnostics?.RecordAllOutcomes(RenderPipelineOutcome.Metadata);
            request.CompleteMetadataOnly();
            RenderRequestDiagnostics.Complete(request);
        }
    }

    private static void FailFamily(
        RenderRequest root,
        RecordedRenderGraph graph,
        Exception exception,
        RenderPipelineFailurePhase failurePhase)
    {
        RenderRequestOwner owner = root.Options.Owner;
        if (owner.PrimaryFailure is null)
            owner.RecordPrimaryFailure(exception);
        owner.Cleanup();

        foreach ((RenderRequest request, _) in EnumerateFamilyDepthFirst(root, graph))
        {
            RenderPipelineDiagnosticRecorder? diagnostics = RenderRequestDiagnostics.TryGet(request);
            diagnostics?.RecordFailure(failurePhase);
            foreach (Exception cleanupFailure in owner.CleanupFailures)
                diagnostics?.RecordCleanupFailure();
            request.FailFamilyMember();
            RenderRequestDiagnostics.Complete(request);
        }
    }

    private static IEnumerable<(RenderRequest Request, RecordedRenderGraph Graph)> EnumerateFamilyDepthFirst(
        RenderRequest root,
        RecordedRenderGraph graph)
    {
        foreach (RecordedNestedRenderRequest nested in graph.NestedRequests)
        {
            foreach ((RenderRequest request, RecordedRenderGraph nestedGraph) in
                     EnumerateFamilyDepthFirst(nested.Request, nested.Graph))
            {
                yield return (request, nestedGraph);
            }
        }

        yield return (root, graph);
    }

    private sealed class FamilyCompilationContext
    {
        public RenderPipelineFailurePhase FailurePhase { get; private set; }

        public void Set(RenderRequest request, RenderPipelineFailurePhase failurePhase)
        {
            ArgumentNullException.ThrowIfNull(request);
            FailurePhase = failurePhase;
        }
    }
}

internal static class TargetDependencyLowerer
{
    public static TargetDependencyPlan Lower(
        ImmutableArray<RenderFragmentReference> roots,
        Rect? rootDomain = null)
    {
        var builder = new Builder();
        TargetScopeId rootScope = builder.CreateScope(
            parentId: null,
            owner: null,
            resolvedDomain: rootDomain);
        foreach (RenderFragmentReference root in roots)
            builder.LowerRoot(root, rootScope);
        return builder.Build();
    }

    private sealed class Builder
    {
        private readonly List<TargetDependencyStep> _steps = [];
        private readonly List<TargetScopePlan> _scopes = [];
        private readonly Dictionary<TargetScopeId, TargetTokenId> _currentTokens = [];
        private readonly HashSet<RenderFragmentReference> _scheduledEffects =
            new(ReferenceEqualityComparer.Instance);
        private int _nextScopeId;
        private int _nextTokenId;

        public TargetScopeId CreateScope(
            TargetScopeId? parentId,
            RenderFragmentReference? owner,
            Rect? resolvedDomain,
            bool inheritParentToken = false,
            bool isOrderOnly = false)
        {
            var scopeId = new TargetScopeId(++_nextScopeId);
            TargetTokenId token = inheritParentToken && parentId is { } parent
                ? _currentTokens[parent]
                : new TargetTokenId(++_nextTokenId);
            _currentTokens.Add(scopeId, token);
            _scopes.Add(new TargetScopePlan(
                scopeId,
                parentId,
                owner?.Id,
                token,
                resolvedDomain,
                isOrderOnly));
            return scopeId;
        }

        public void LowerRoot(
            RenderFragmentReference reference,
            TargetScopeId scopeId,
            bool compositeOutput = true)
        {
            switch (reference.Kind)
            {
                case RenderFragmentKind.Layer:
                    LowerFiniteLayer(reference, scopeId, compositeOutput);
                    return;
                case RenderFragmentKind.TargetLayerScope:
                    LowerTargetLayerScope(reference, scopeId);
                    return;
                case RenderFragmentKind.TargetCapture:
                case RenderFragmentKind.BuiltInBackdropCapture:
                    LowerCapture(reference, scopeId);
                    if (compositeOutput && reference.ContributesValuesToTarget)
                        AddStep(reference, scopeId, TargetDependencyKind.Composite, FirstInputValue(reference), null);
                    return;
                case RenderFragmentKind.TargetCommand:
                case RenderFragmentKind.RawTargetCommand:
                    ValidateCommandDomain(reference, scopeId);
                    LowerCommand(reference, scopeId);
                    return;
                case RenderFragmentKind.TargetScope:
                    LowerScopeWrapper(reference, scopeId, compositeOutput);
                    return;
                case RenderFragmentKind.RawTargetScope:
                    ValidateFullDomain(reference, scopeId);
                    LowerScopeWrapper(reference, scopeId, compositeOutput);
                    return;
                case RenderFragmentKind.ContributeValues:
                    LowerDependencies(reference, scopeId);
                    if (compositeOutput)
                    {
                        AddStep(
                            reference,
                            scopeId,
                            TargetDependencyKind.Composite,
                            FirstInputValue(reference),
                            null);
                    }
                    return;
                case RenderFragmentKind.Blend:
                case RenderFragmentKind.Opacity:
                    LowerScopeWrapper(reference, scopeId, compositeOutput);
                    return;
                case RenderFragmentKind.OpacityMask:
                    LowerOpacityMask(reference, scopeId, compositeOutput);
                    return;
                default:
                    LowerDependencies(reference, scopeId);
                    if (compositeOutput && reference.ContributesValuesToTarget)
                        AddStep(reference, scopeId, TargetDependencyKind.Composite, FirstValue(reference), null);
                    return;
            }
        }

        public TargetDependencyPlan Build() => new([.. _steps], [.. _scopes]);

        private void LowerFiniteLayer(
            RenderFragmentReference reference,
            TargetScopeId parentScope,
            bool compositeOutput)
        {
            Rect domain = ((LayerRenderFragmentPayload)reference.Payload!).Domain
                ?? reference.Bounds;
            TargetScopeId childScope = CreateScope(
                parentScope,
                reference,
                domain);
            foreach (RenderFragmentReference input in reference.Inputs)
                LowerRoot(input, childScope);

            if (compositeOutput && reference.ContributesValuesToTarget)
            {
                AddStep(
                    reference,
                    parentScope,
                    TargetDependencyKind.ScopeComposite,
                    FirstValue(reference),
                    null);
            }
        }

        private void LowerTargetLayerScope(
            RenderFragmentReference reference,
            TargetScopeId parentScope)
        {
            TargetRegion region = ((TargetLayerScopeRenderFragmentPayload)reference.Payload!).Region;
            Rect domain = ResolveRegion(region, GetDomain(parentScope), reference);
            bool isOrderOnly = region.Kind == TargetRegionKind.Empty;
            TargetScopeId childScope = CreateScope(
                parentScope,
                reference,
                domain,
                isOrderOnly: isOrderOnly);
            if (isOrderOnly)
                return;

            foreach (RenderFragmentReference input in reference.Inputs)
                LowerRoot(input, childScope);

            AddStep(
                reference,
                parentScope,
                TargetDependencyKind.ScopeComposite,
                FirstValue(reference),
                null);
        }

        private void LowerScopeWrapper(
            RenderFragmentReference reference,
            TargetScopeId scopeId,
            bool compositeOutput)
        {
            Rect? authoredDomain = MapDomainIntoScope(reference, GetDomain(scopeId));
            TargetScopeId authoredScope = CreateScope(
                scopeId,
                reference,
                authoredDomain,
                inheritParentToken: true);
            bool childHasEffects = false;
            foreach (RenderFragmentReference input in reference.Inputs)
            {
                if (input.HasTargetEffects)
                {
                    childHasEffects = true;
                    LowerRoot(input, authoredScope, compositeOutput);
                }
            }

            if (compositeOutput && reference.ContributesValuesToTarget && !childHasEffects)
            {
                AddStep(reference, authoredScope, TargetDependencyKind.Composite, FirstValue(reference), null);
            }

            _currentTokens[scopeId] = _currentTokens[authoredScope];
        }

        private void LowerOpacityMask(
            RenderFragmentReference reference,
            TargetScopeId scopeId,
            bool compositeOutput)
        {
            for (int i = 1; i < reference.Inputs.Length; i++)
            {
                RenderFragmentReference dependency = reference.Inputs[i];
                if (dependency.HasTargetEffects)
                    LowerRoot(dependency, scopeId, compositeOutput: false);
            }

            Rect? authoredDomain = MapDomainIntoScope(reference, GetDomain(scopeId));
            TargetScopeId authoredScope = CreateScope(
                scopeId,
                reference,
                authoredDomain,
                inheritParentToken: true);
            bool childHasEffects = false;
            if (!reference.Inputs.IsDefaultOrEmpty)
            {
                RenderFragmentReference primary = reference.Inputs[0];
                if (primary.HasTargetEffects)
                {
                    childHasEffects = true;
                    LowerRoot(primary, authoredScope, compositeOutput);
                }
            }

            if (compositeOutput && reference.ContributesValuesToTarget && !childHasEffects)
            {
                AddStep(reference, authoredScope, TargetDependencyKind.Composite, FirstValue(reference), null);
            }

            _currentTokens[scopeId] = _currentTokens[authoredScope];
        }

        private void ValidateCaptureDomain(
            RenderFragmentReference reference,
            TargetScopeId scopeId)
        {
            TargetRegion region = reference.Payload switch
            {
                TargetCaptureRenderFragmentPayload capture => capture.Description.SourceRegion,
                BuiltInBackdropCaptureRenderFragmentPayload capture => capture.Description.SourceRegion,
                _ => throw new InvalidOperationException("The target-capture payload is invalid."),
            };
            _ = ResolveRegion(region, GetDomain(scopeId), reference);
        }

        private void ValidateCommandDomain(
            RenderFragmentReference reference,
            TargetScopeId scopeId)
        {
            TargetRegion region = reference.Payload switch
            {
                TargetCommandRenderFragmentPayload command => command.Description.AffectedRegion,
                RawTargetCommandRenderFragmentPayload => TargetRegion.Full,
                _ => throw new InvalidOperationException("The target-command payload is invalid."),
            };
            _ = ResolveRegion(region, GetDomain(scopeId), reference);
        }

        private void ValidateFullDomain(
            RenderFragmentReference reference,
            TargetScopeId scopeId)
            => _ = ResolveRegion(TargetRegion.Full, GetDomain(scopeId), reference);

        private void LowerCommand(RenderFragmentReference reference, TargetScopeId scopeId)
        {
            if (!_scheduledEffects.Add(reference))
                return;
            LowerDependencies(reference, scopeId);
            AddStep(reference, scopeId, TargetDependencyKind.Command, FirstInputValue(reference), null);
        }

        private void LowerCapture(RenderFragmentReference reference, TargetScopeId scopeId)
        {
            if (!_scheduledEffects.Add(reference))
                return;
            ValidateCaptureDomain(reference, scopeId);
            RenderValueId? capturedValue = FirstValue(reference);
            AddStep(reference, scopeId, TargetDependencyKind.Capture, capturedValue, capturedValue);
        }

        private void LowerDependencies(
            RenderFragmentReference reference,
            TargetScopeId scopeId)
        {
            foreach (RenderFragmentReference input in reference.Inputs)
            {
                if (!input.HasTargetEffects)
                    continue;

                LowerRoot(input, scopeId, compositeOutput: false);
            }
        }

        private void AddStep(
            RenderFragmentReference reference,
            TargetScopeId scopeId,
            TargetDependencyKind kind,
            RenderValueId? targetReadValueId,
            RenderValueId? producedValueId)
        {
            RenderFragmentId fragmentId = reference.Id
                ?? throw new InvalidOperationException("A target dependency refers to an uncommitted fragment.");
            TargetTokenId input = _currentTokens[scopeId];
            var output = new TargetTokenId(++_nextTokenId);
            _currentTokens[scopeId] = output;
            _steps.Add(new TargetDependencyStep(
                fragmentId,
                scopeId,
                input,
                output,
                targetReadValueId,
                producedValueId,
                kind));
        }

        private static RenderValueId? FirstValue(RenderFragmentReference reference)
            => reference.ValueIds.IsDefaultOrEmpty ? null : reference.ValueIds[0];

        private static RenderValueId? FirstInputValue(RenderFragmentReference reference)
        {
            foreach (RenderFragmentReference input in reference.Inputs)
            {
                if (!input.ValueIds.IsDefaultOrEmpty)
                    return input.ValueIds[0];
            }

            return null;
        }

        private Rect? GetDomain(TargetScopeId scopeId)
            => _scopes.Single(scope => scope.Id == scopeId).ResolvedDomain;

        private static Rect? MapDomainIntoScope(
            RenderFragmentReference reference,
            Rect? parentDomain)
        {
            if (parentDomain is not { } domain)
                return null;

            return reference.Payload switch
            {
                TargetScopeRenderFragmentPayload scope
                    => scope.Description.Bounds.GetRequiredInputBounds(domain),
                RawTargetScopeRenderFragmentPayload scope
                    => scope.Description.Bounds.GetRequiredInputBounds(domain),
                _ => domain,
            };
        }

        private static Rect ResolveRegion(
            TargetRegion region,
            Rect? ownerDomain,
            RenderFragmentReference owner)
        {
            return region.Kind switch
            {
                TargetRegionKind.Empty => Rect.Empty,
                TargetRegionKind.Region => region.Value,
                TargetRegionKind.Full when ownerDomain is { } domain => domain,
                TargetRegionKind.Full => throw new InvalidOperationException(
                    $"A reachable Full target access on {owner.Kind} requires a finite owning target domain."),
                _ => throw new InvalidOperationException("The target region is uninitialized."),
            };
        }
    }
}
