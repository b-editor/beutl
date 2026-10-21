using System.Reactive;

using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Fusion;

// A filter-effect segment collects whatever could not lower to a typed fragment — Skia items and typed
// suffixes as well as custom effects — so its island reports a custom effect only when it really holds one.
[TestFixture]
public sealed class FilterEffectSegmentBoundaryReasonTests
{
    private static readonly Rect s_bounds = new(0, 0, 24, 16);

    [Test]
    public void A_segment_without_a_custom_effect_does_not_blame_one()
    {
        using CompiledRenderRequest compiled = Compile(new Blur { Sigma = { Value = new(2, 2) } });

        Assert.That(SegmentIslandPayload(compiled).HasImperativeItem, Is.False);
    }

    [Test]
    public void A_segment_holding_a_custom_effect_still_names_it()
    {
        using CompiledRenderRequest compiled = Compile(new StrokeEffect());

        Assert.That(SegmentIslandPayload(compiled).HasImperativeItem, Is.True);
    }

    [Test]
    public void PureSkiaSegments_DeclareSingleForOneInput()
    {
        using CompiledRenderRequest imageFilter = Compile(
            new Blur { Sigma = { Value = new(2, 2) } });
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
            new Blur { Sigma = { Value = new(2, 2) } },
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
            .Single(static fragment => fragment.Kind == RenderFragmentKind.FilterEffectSegment);

    // The segment runs as its own semantic island, and its payload says whether it holds a custom effect.
    private static FilterEffectSegmentRenderFragmentPayload SegmentIslandPayload(CompiledRenderRequest compiled)
    {
        ExecutionIsland island = compiled.ExecutionPlan.Islands.Single(island =>
            island.ShaderRun is null
            && compiled.Graph.Fragments[island.FragmentIndices[0]].Kind == RenderFragmentKind.FilterEffectSegment);
        return (FilterEffectSegmentRenderFragmentPayload)compiled.Graph.Fragments[island.FragmentIndices[0]].Payload!;
    }

    private static CompiledRenderRequest Compile(FilterEffect effect, int childCount = 1)
        => FusionBoundaryExecutionTestSupport.CompileEffect(
            effect,
            s_bounds,
            SkslBackendBudgetResolver.Portable,
            childCount: childCount);
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
