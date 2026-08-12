using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Fusion;

// A filter-effect segment collects whatever could not lower to a typed fragment — Skia items and typed
// suffixes as well as custom effects — so the boundary reason has to name what the segment really holds.
[TestFixture]
public sealed class FilterEffectSegmentBoundaryReasonTests
{
    private static readonly Rect s_bounds = new(0, 0, 24, 16);

    [Test]
    public void A_segment_without_a_custom_effect_does_not_blame_one()
    {
        using CompiledRenderRequest compiled = Compile(new Blur { Sigma = { CurrentValue = new(2, 2) } });

        Assert.Multiple(() =>
        {
            Assert.That(Reasons(compiled), Does.Contain(ExecutionIslandBoundaryReason.FilterEffectSegment));
            Assert.That(Reasons(compiled), Does.Not.Contain(ExecutionIslandBoundaryReason.LegacyCustomEffect));
        });
    }

    [Test]
    public void A_segment_holding_a_custom_effect_still_names_it()
    {
        using CompiledRenderRequest compiled = Compile(new StrokeEffect());

        Assert.That(Reasons(compiled), Does.Contain(ExecutionIslandBoundaryReason.LegacyCustomEffect));
    }

    private static IEnumerable<ExecutionIslandBoundaryReason> Reasons(CompiledRenderRequest compiled)
        => compiled.ExecutionPlan.Boundaries.Select(static boundary => boundary.Reason);

    private static CompiledRenderRequest Compile(FilterEffect effect)
    {
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var node = new FilterEffectRenderNode(resource);
        node.AddChild(new EllipseRenderNode(s_bounds, Brushes.Resource.White, null));

        var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            targetDomain: s_bounds,
            cachePolicy: RenderCacheOptions.Disabled));
        try
        {
            RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
            return new RenderRequestCompiler().Compile(request, graph);
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }
}
