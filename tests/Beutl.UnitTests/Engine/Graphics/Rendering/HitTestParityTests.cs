using Beutl.Composition;
using Beutl.Graphics;
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
                OutputScale = outputScale,
                UseRenderCache = false,
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
}
