using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// Hit-testing is in LOGICAL coordinates and independent of the output scale, so the same logical point hits
// the same content at every render scale.
[TestFixture]
public class HitTestParityTests
{
    private static readonly Rect s_filteredEllipse = new(0, 0, 100, 100);

    private static bool HitEllipse(float outputScale, Point point)
    {
        var rect = new Rect(0, 0, 100, 80);
        using var fill = new SolidColorBrush(Colors.Red).ToResource(CompositionContext.Default);
        using var node = new EllipseRenderNode(rect, fill, null);
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            OutputScale = outputScale,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
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

        Assert.Multiple(() =>
        {
            Assert.That(HitEllipse(outputScale, inside), Is.True, "inside point should hit");
            Assert.That(HitEllipse(outputScale, outside), Is.False, "outside point should miss");
        });
    }

    // Threshold is the one built-in CurrentPixel stage that paints a fully transparent input pixel, so its forwarded
    // hit test must still hit there; the other two cases keep the forwarded test from claiming the whole output.
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
                FilteredShapeHarness.HitTest(s_filteredEllipse, painting, corner),
                Is.True,
                "the corner carries a visible half-opaque grey here, so it has to be selectable");
            Assert.That(
                FilteredShapeHarness.HitTest(s_filteredEllipse, new Threshold(), corner),
                Is.False,
                "at the defaults the same corner stays fully transparent");
            Assert.That(
                FilteredShapeHarness.HitTest(s_filteredEllipse, new Invert(), corner),
                Is.False,
                "an alpha-preserving stage adds no coverage, so it answers with its input's");
        });
    }

    [TestCase(
        "half4 apply(half4 color) { return half4(1.0); }",
        TestName = "CurrentPixelScript_HitsTransparentEllipseCorner")]
    [TestCase(
        "half4 main(float2 fragCoord) { return half4(1.0); }",
        TestName = "WholeSourceScript_HitsTransparentEllipseCorner")]
    public void AlphaSynthesizingScript_HitsTransparentEllipseCorner(string script)
    {
        var effect = new SKSLScriptEffect
        {
            Script = { CurrentValue = script },
        };

        Assert.That(FilteredShapeHarness.HitTest(s_filteredEllipse, effect, new Point(2, 2)), Is.True);
    }
}
