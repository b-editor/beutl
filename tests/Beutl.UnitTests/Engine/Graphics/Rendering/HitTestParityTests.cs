using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// US4 / T058: hit-testing is in LOGICAL coordinates and independent of the output scale, so the same
// logical point hits the same content at every render scale.
[TestFixture]
public class HitTestParityTests
{
    private static bool HitEllipse(float outputScale, Point point)
    {
        var rect = new Rect(0, 0, 100, 80);
        using var fill = new SolidColorBrush(Colors.Red).ToResource(CompositionContext.Default);
        using var node = new EllipseRenderNode(rect, fill, null);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    OutputScale = outputScale,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });
        return renderer.HitTest(point);
    }

    [TestCase(0.25f)]
    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void HitTest_SameLogicalPoint_SameResultAtEveryScale(float outputScale)
    {
        // One logical point inside the ellipse, one outside; both must agree across scales.
        var inside = new Point(50, 40);
        var outside = new Point(2, 2);
        bool insideAtOne = HitEllipse(1, inside);
        bool outsideAtOne = HitEllipse(1, outside);
        bool insideAtScale = HitEllipse(outputScale, inside);
        bool outsideAtScale = HitEllipse(outputScale, outside);

        Assert.Multiple(() =>
        {
            Assert.That(insideAtScale, Is.EqualTo(insideAtOne), "inside-point parity");
            Assert.That(outsideAtScale, Is.EqualTo(outsideAtOne), "outside-point parity");
            Assert.That(insideAtScale, Is.True, "inside point should hit");
            Assert.That(outsideAtScale, Is.False, "outside point should miss");
        });
    }

    // A current-pixel stage answers a hit test by forwarding it to its input, which is exactly right for the
    // stages that only recolour the pixels they were handed and far more accurate than testing the output
    // rectangle. Threshold is the one built-in whose entry point can return alpha for a fully transparent
    // pixel: it returns half4(t) without consulting the input alpha, so below Value = Smoothness / 2 it fills
    // the whole output with visible mid-grey that the forwarded test would refuse to hit. The other two cases
    // are the half that has to keep failing - a stage that claims its output rectangle unconditionally would
    // satisfy the first assertion while hitting thin air everywhere else.
    [Test]
    public void ACurrentPixelStage_HitsWhatItPaintsAndNotWhatItLeavesTransparent()
    {
        var corner = new Point(2, 2);
        var painting = new Threshold
        {
            Value = { CurrentValue = 0 },
            Smoothness = { CurrentValue = 50 },
            Strength = { CurrentValue = 100 },
        };

        Assert.Multiple(() =>
        {
            Assert.That(
                HitFilteredEllipse(painting, corner),
                Is.True,
                "the corner carries a visible half-opaque grey here, so it has to be selectable");
            Assert.That(
                HitFilteredEllipse(new Threshold(), corner),
                Is.False,
                "at the defaults the same corner stays fully transparent");
            Assert.That(
                HitFilteredEllipse(new Invert(), corner),
                Is.False,
                "an alpha-preserving stage adds no coverage, so it answers with its input's");
        });
    }

    private static bool HitFilteredEllipse(FilterEffect effect, Point point)
    {
        using var node = new FilterEffectRenderNode(effect.ToResource(CompositionContext.Default));
        node.AddChild(new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null));
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    OutputScale = 1f,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });
        return renderer.HitTest(point);
    }
}
