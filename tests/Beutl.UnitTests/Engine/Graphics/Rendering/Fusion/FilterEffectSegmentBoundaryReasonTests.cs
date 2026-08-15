using System.Reactive;

using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

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

    [Test]
    public void PureSkiaSegments_DeclareSingleForOneInput()
    {
        using CompiledRenderRequest imageFilter = Compile(
            new Blur { Sigma = { CurrentValue = new(2, 2) } });
        using CompiledRenderRequest colorFilter = Compile(new PureSkiaColorFilterEffect());

        Assert.Multiple(() =>
        {
            Assert.That(Segment(imageFilter).ValueCardinality, Is.EqualTo(RenderValueCardinality.Single));
            Assert.That(Segment(colorFilter).ValueCardinality, Is.EqualTo(RenderValueCardinality.Single));
        });
    }

    [Test]
    public void CustomSegment_RemainsDynamic()
    {
        using CompiledRenderRequest compiled = Compile(new SplitEffect());

        Assert.That(Segment(compiled).ValueCardinality, Is.EqualTo(RenderValueCardinality.Dynamic));
    }

    [Test]
    public void PureSkiaSegment_WithMultipleInputs_RemainsDynamic()
    {
        using CompiledRenderRequest compiled = Compile(
            new Blur { Sigma = { CurrentValue = new(2, 2) } },
            childCount: 2);

        Assert.That(Segment(compiled).ValueCardinality, Is.EqualTo(RenderValueCardinality.Dynamic));
    }

    [Test]
    public void PureSkiaSegment_WithEmptyOutput_IsZeroOrOne()
    {
        using CompiledRenderRequest compiled = Compile(new EmptySkiaFilterEffect());

        Assert.That(Segment(compiled).ValueCardinality, Is.EqualTo(RenderValueCardinality.ZeroOrOne));
    }

    private static RenderFragmentReference Segment(CompiledRenderRequest compiled)
        => compiled.Graph.Fragments
            .Select(static fragment => (RenderFragmentReference)fragment.Payload!)
            .Single(static fragment => fragment.Kind == RenderFragmentKind.FilterEffectSegment);

    private static IEnumerable<ExecutionIslandBoundaryReason> Reasons(CompiledRenderRequest compiled)
        => compiled.ExecutionPlan.Boundaries.Select(static boundary => boundary.Reason);

    private static CompiledRenderRequest Compile(FilterEffect effect, int childCount = 1)
    {
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var node = new FilterEffectRenderNode(resource);
        for (int index = 0; index < childCount; index++)
        {
            node.AddChild(new EllipseRenderNode(
                s_bounds.Translate(new Vector(index, 0)),
                Brushes.Resource.White,
                null));
        }

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

internal sealed partial class EmptySkiaFilterEffect : FilterEffect
{
    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        context.AppendSkiaFilter(
            data: 0,
            factory: static (_, input, _) => input,
            transformBounds: static (_, _) => Rect.Empty);
    }
}

internal sealed partial class PureSkiaColorFilterEffect : FilterEffect
{
    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        context.AppendSKColorFilter(
            Unit.Default,
            static (_, _) => SKColorFilter.CreateLumaColor());
    }
}
