using System.Collections.Immutable;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed class RenderRequestCompiler
{
    private readonly StructuralPlanCache? _structuralPlanCache;
    private readonly RenderCacheResolutionContext? _renderCacheContext;
    private readonly IRenderCacheLookup? _renderCacheLookup;

    /// <summary>
    /// Counts the full <see cref="RegionAnalyzer.Analyze"/> runs this compiler drove, so a test can pin how
    /// many region analyses one request pays for.
    /// </summary>
    internal int RegionAnalysisCount { get; private set; }

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
        try
        {
            var measurements = new Dictionary<RenderRequest, RenderNodeMeasurement>(
                ReferenceEqualityComparer.Instance);
            ResolveMetadataFamily(request, graph, measurements);
            if (request.Options.Purpose is RenderRequestPurpose.Bounds or RenderRequestPurpose.HitTest)
                CompleteMetadataFamily(request, graph);
            return measurements[request];
        }
        catch (Exception ex)
        {
            FailFamily(request, graph, ex);
            throw;
        }
    }

    public CompiledRenderRequest Compile(
        RenderRequest request,
        RecordedRenderGraph graph)
        => Compile(request, graph, SkslBackendBudgetResolver.Portable);

    internal CompiledRenderRequest Compile(
        RenderRequest request,
        RecordedRenderGraph graph,
        SkslBackendBudget shaderBudget)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(shaderBudget);
        try
        {
            var measurements = new Dictionary<RenderRequest, RenderNodeMeasurement>(
                ReferenceEqualityComparer.Instance);
            ResolveMetadataFamily(request, graph, measurements);
            int nextStructuralPlanSlot = 0;
            CompiledRenderRequest compiled = CompileFamily(
                request,
                graph,
                measurements,
                shaderBudget,
                ref nextStructuralPlanSlot);
            _structuralPlanCache?.RetainFamilySlots(nextStructuralPlanSlot);
            return compiled;
        }
        catch (Exception ex)
        {
            FailFamily(request, graph, ex);
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
            SkslBackendBudgetResolver.Portable);

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

        try
        {
            var measurements = new Dictionary<RenderRequest, RenderNodeMeasurement>(
                ReferenceEqualityComparer.Instance)
            {
                [request] = measurement,
            };
            CollectNestedMetadata(graph, measurements);
            int nextStructuralPlanSlot = 0;
            CompiledRenderRequest compiled = CompileFamily(
                request,
                graph,
                measurements,
                shaderBudget,
                ref nextStructuralPlanSlot);
            _structuralPlanCache?.RetainFamilySlots(nextStructuralPlanSlot);
            return compiled;
        }
        catch (Exception ex)
        {
            FailFamily(request, graph, ex);
            throw;
        }
    }

    private void ResolveMetadataFamily(
        RenderRequest request,
        RecordedRenderGraph graph,
        IDictionary<RenderRequest, RenderNodeMeasurement> measurements)
    {
        foreach (RecordedNestedRenderRequest nested in graph.NestedRequests)
            ResolveMetadataFamily(nested.Request, nested.Graph, measurements);

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
        RenderNodeMeasurement measurement = new RegionAnalyzer()
            .ResolveMeasurement(request.Options, roots, targetDependencies);
        request.TransitionTo(RenderRequestState.MetadataResolved);
        measurements.Add(request, measurement);
    }

    private void CollectNestedMetadata(
        RecordedRenderGraph graph,
        IDictionary<RenderRequest, RenderNodeMeasurement> measurements)
    {
        foreach (RecordedNestedRenderRequest nested in graph.NestedRequests)
        {
            if (nested.Request.State == RenderRequestState.Recorded)
            {
                ResolveMetadataFamily(nested.Request, nested.Graph, measurements);
            }
            else if (nested.Request.State == RenderRequestState.MetadataResolved)
            {
                CollectNestedMetadata(nested.Graph, measurements);
                ImmutableArray<RenderFragmentReference> roots = ResolveRoots(nested.Graph);
                TargetDependencyPlan targetDependencies = TargetDependencyLowerer.Lower(
                    roots,
                    nested.Request.Options.TargetDomain);
                measurements[nested.Request] = new RegionAnalyzer()
                    .ResolveMeasurement(nested.Request.Options, roots, targetDependencies);
            }
            else
            {
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
                ref nextStructuralPlanSlot));
        }

        int structuralPlanSlot = nextStructuralPlanSlot++;
        return CompileSingle(
            request,
            graph,
            measurements[request],
            shaderBudget,
            nested.MoveToImmutable(),
            structuralPlanSlot);
    }

    private CompiledRenderRequest CompileSingle(
        RenderRequest request,
        RecordedRenderGraph graph,
        RenderNodeMeasurement measurement,
        SkslBackendBudget shaderBudget,
        ImmutableArray<CompiledRenderRequest> nestedRequests,
        int structuralPlanSlot)
    {
        if (request.State != RenderRequestState.MetadataResolved)
        {
            throw new InvalidOperationException(
                "A render request can be compiled only after metadata resolution.");
        }

        ImmutableArray<RenderFragmentReference> roots = ResolveRoots(graph);
        // Metadata resolution mutates symbolic fragment bounds used by target-scope lowering.
        // Re-lower here so the final plan uses those resolved owning domains; the preliminary
        // plan used to resolve metadata is not safe to reuse.
        TargetDependencyPlan targetDependencies = TargetDependencyLowerer.Lower(
            roots,
            request.Options.TargetDomain);
        RegionAnalysisCount++;
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
        RenderCacheResolutionContext cacheContext = _renderCacheContext
            ?? new RenderCacheResolutionContext(
                RenderCacheFormatIdentity.LinearPremultipliedRgba16Float,
                new RenderCacheDeviceContextIdentity(request, request),
                allowPersistentLookup: false,
                allowCapturePublication: false);
        RenderCachePlanningResult cachePlanning = new RenderCacheResolver().Resolve(
            request,
            graph,
            regions,
            roots,
            cacheContext,
            _renderCacheLookup);
        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> materializationDemands =
            cachePlanning.MaterializationDemands;
        IReadOnlySet<RenderFragmentReference> previewDropEligibleMaterializations =
            cachePlanning.PreviewDropEligibleMaterializations;
        RenderCacheResolution cacheResolution = cachePlanning.Resolution;
        request.TransitionTo(RenderRequestState.CachesResolved);
        ExecutionIslandPlan executionPlan;
        if (_structuralPlanCache is not null)
        {
            var planning = (
                Graph: graph,
                Roots: roots,
                CacheResolution: cacheResolution,
                FusionMode: request.Options.FusionMode,
                ShaderBudget: shaderBudget);
            StructuralPlanIdentity structuralIdentity = StructuralPlanIdentity.Create(
                request.Options.PlanIdentity,
                graph,
                shaderBudget,
                cacheResolution);
            executionPlan = _structuralPlanCache.GetOrCompile(
                structuralIdentity,
                planning,
                static state => new ExecutionIslandPlanner().Plan(
                    state.Graph,
                    state.Roots,
                    state.CacheResolution,
                    state.FusionMode,
                    state.ShaderBudget),
                familySlot: structuralPlanSlot);
        }
        else
        {
            executionPlan = new ExecutionIslandPlanner().Plan(
                graph,
                roots,
                cacheResolution,
                request.Options.FusionMode,
                shaderBudget);
        }

        request.TransitionTo(RenderRequestState.Planned);
        return new CompiledRenderRequest(
            request,
            graph,
            regions,
            roots,
            materializationDemands,
            previewDropEligibleMaterializations,
            targetDependencies,
            cacheResolution,
            executionPlan,
            nestedRequests);
    }

    internal static ImmutableArray<RenderFragmentReference> ResolveRoots(
        RecordedRenderGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (graph.PublicationRoots.IsDefaultOrEmpty)
            return [];

        var roots = ImmutableArray.CreateBuilder<RenderFragmentReference>(graph.PublicationRoots.Length);
        foreach (RenderFragmentId id in graph.PublicationRoots)
            roots.Add(graph.GetFragment(id));

        return roots.MoveToImmutable();
    }

    private static void CompleteMetadataFamily(RenderRequest root, RecordedRenderGraph graph)
    {
        foreach ((RenderRequest request, _) in EnumerateFamilyDepthFirst(root, graph))
            request.CompleteMetadataOnly();
    }

    private static void FailFamily(
        RenderRequest root,
        RecordedRenderGraph graph,
        Exception exception)
    {
        RenderRequestOwner owner = root.Options.Owner;
        if (owner.PrimaryFailure is null)
            owner.RecordPrimaryFailure(exception);
        owner.Cleanup();

        foreach ((RenderRequest request, _) in EnumerateFamilyDepthFirst(root, graph))
            request.FailFamilyMember();
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
}
