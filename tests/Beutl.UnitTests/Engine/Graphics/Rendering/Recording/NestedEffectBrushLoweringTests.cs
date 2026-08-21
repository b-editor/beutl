using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

/// <summary>
/// Pins that a brush owned by a nested filter effect is lowered into the effect's own segment.
/// </summary>
/// <remarks>
/// A <see cref="DrawableBrush"/> draws a whole drawable, so it is the case most likely to be hoisted out into a
/// second stream input or a nested request. Either would split one effect across two recorded units and change
/// what the planner may fuse, so the recorded shape has to stay the same as a brush-free effect's.
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class NestedEffectBrushLoweringTests
{
    private static IEnumerable<TestCaseData> NestedEffects()
    {
        yield return new TestCaseData(new Func<FilterEffect>(static () => new Blur()))
            .SetArgDisplayNames("no brush");
        yield return new TestCaseData(new Func<FilterEffect>(MakeDrawableBrushShadow))
            .SetArgDisplayNames("drawable brush");
    }

    [TestCaseSource(nameof(NestedEffects))]
    public void NestedEffect_RecordsOneSegmentOverOneStreamWithoutANestedRequest(Func<FilterEffect> factory)
    {
        RecordGraph(MakeDelayed(factory()), graph =>
        {
            FilterEffectSegmentRenderFragmentPayload[] segments = SegmentsOf(graph);
            Assert.Multiple(() =>
            {
                Assert.That(segments, Has.Length.EqualTo(1),
                    "A nested effect must record as one segment however its brush draws.");
                Assert.That(segments[0].StreamInputCount, Is.EqualTo(1),
                    "A brush must not become a second stream input.");
                Assert.That(graph.NestedRequests, Is.Empty,
                    "A brush must not be hoisted into its own request.");
            });
        });
    }

    private static void RecordGraph(FilterEffect effect, Action<RecordedRenderGraph> assert)
    {
        using var root = new FilterEffectRenderNode(effect.ToResource(CompositionContext.Default));
        root.AddChild(new RectangleRenderNode(new Rect(0, 0, 40, 30), Brushes.Resource.White, null));
        using var owner = new RenderRequestOwner();
        using var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            maxWorkingScale: 1,
            owner: owner));

        assert(new RenderRequestRecorder(request).Record(root));
    }

    private static FilterEffectSegmentRenderFragmentPayload[] SegmentsOf(RecordedRenderGraph graph)
        => [.. graph.Fragments
            .Select(static fragment => (RenderFragmentReference)fragment.Payload!)
            .Where(static reference => reference.Kind == RenderFragmentKind.FilterEffectSegment)
            .Select(static reference => (FilterEffectSegmentRenderFragmentPayload)reference.Payload!)];

    private static DelayAnimationEffect MakeDelayed(FilterEffect child)
    {
        var delay = new DelayAnimationEffect();
        delay.Delay.CurrentValue = 0f;
        delay.Effect.CurrentValue = child;
        return delay;
    }

    private static FilterEffect MakeDrawableBrushShadow()
    {
        var content = new RectShape();
        content.Width.CurrentValue = 10;
        content.Height.CurrentValue = 10;
        content.Fill.CurrentValue = Brushes.White;
        var brush = new DrawableBrush(content);
        brush.Stretch.CurrentValue = Stretch.Fill;

        var shadow = new FlatShadow();
        shadow.Length.CurrentValue = 4;
        shadow.Brush.CurrentValue = brush;
        return shadow;
    }
}
