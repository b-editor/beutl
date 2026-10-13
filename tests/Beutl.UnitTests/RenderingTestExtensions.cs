using System.Collections.Immutable;
using System.Reflection;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using Beutl.Media;

namespace Beutl.UnitTests;

// Test-side conveniences for members the render pipeline itself does not need.
internal static class RenderingTestExtensions
{
    private static readonly FieldInfo s_secondaryFailures = typeof(RenderRequestOwner)
        .GetField("_secondaryFailures", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly FieldInfo s_bindingState = typeof(NestedRenderTargetBinding)
        .GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly FieldInfo s_identityBounds = typeof(RenderOutputCacheIdentity)
        .GetField("_bounds", BindingFlags.Instance | BindingFlags.NonPublic)!;

    extension(ExecutionIslandPlan plan)
    {
        public IEnumerable<CompiledShaderRun> ShaderRuns
            => plan.Islands
                .Where(static island => island.ShaderRun is not null)
                .Select(static island => island.ShaderRun!);
    }

    extension(RenderRequestCompiler compiler)
    {
        public CompiledRenderRequest Compile(RenderRequest request, RecordedRenderGraph graph)
            => compiler.Compile(request, graph, SkslBackendBudgetResolver.Portable);
    }

    extension(RegionAnalyzer analyzer)
    {
        public RenderNodeMeasurement ResolveMeasurement(
            RenderRequestOptions options,
            IReadOnlyList<RenderFragmentReference> roots)
            => analyzer.ResolveMeasurement(options, roots, Lower(options, roots));

        public RegionAnalysis Analyze(RenderRequestOptions options, IReadOnlyList<RenderFragmentReference> roots)
            => analyzer.Analyze(options, roots, Lower(options, roots));
    }

    extension(RecordedRenderGraphBuilder builder)
    {
        public RenderFragmentId AddFragment(RenderFragmentReference reference)
        {
            builder.Append(new NodeRecordingCommit(
                [new RecordedRenderFragmentEntry(reference, reference)],
                [],
                [],
                [],
                [],
                []));
            return reference.Id!.Value;
        }

        public RenderCacheCandidateId AddCacheCandidate(RenderFragmentId fragmentId, object cacheKey)
            => builder.AddCacheCandidate(fragmentId, cacheKey, new ContainerRenderNode().Cache);
    }

    extension(RenderFragmentOutputIdentity)
    {
        public static RenderFragmentOutputIdentity Create(
            RenderFragmentReference reference,
            RenderRequestId graphRequestId,
            IReadOnlyDictionary<RenderFragmentReference, EffectiveScale>? materializationDemands = null)
            => RenderFragmentOutputIdentity.Create(
                reference,
                graphRequestId,
                materializationDemands,
                new Dictionary<RenderFragmentReference, RenderFragmentOutputIdentity>(
                    ReferenceEqualityComparer.Instance),
                outputScale: 1,
                maxWorkingScale: float.PositiveInfinity);
    }

    extension(RenderOutputCacheIdentity identity)
    {
        public Rect Bounds => (Rect)s_identityBounds.GetValue(identity)!;
    }

    extension(RenderRequestOwner owner)
    {
        public ImmutableArray<Exception> SecondaryFailures
            => s_secondaryFailures.GetValue(owner) is List<Exception> failures ? [.. failures] : [];
    }

    extension(NestedRenderTargetBinding binding)
    {
        public PixelRect DeviceBounds => PixelRect.FromRect(binding.LogicalBounds, binding.Density);

        public bool IsReady
            => (NestedRenderTargetBindingState)s_bindingState.GetValue(binding)!
               == NestedRenderTargetBindingState.Ready;
    }

    private static TargetDependencyPlan Lower(
        RenderRequestOptions options,
        IReadOnlyList<RenderFragmentReference> roots)
        => TargetDependencyLowerer.Lower([.. roots], options.TargetDomain);
}
