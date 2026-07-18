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
    private static RenderNodeOperation BuildEllipseOp(float outputScale)
    {
        var rect = new Rect(0, 0, 100, 80);
        var fill = new SolidColorBrush(Colors.Red).ToResource(CompositionContext.Default);
        var node = new EllipseRenderNode(rect, fill, null);
        var context = new RenderNodeContext([], RenderIntent.Delivery, outputScale);
        return node.Process(context)[0];
    }

    [TestCase(0.25f)]
    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void HitTest_SameLogicalPoint_SameResultAtEveryScale(float outputScale)
    {
        RenderNodeOperation atOne = BuildEllipseOp(1f);
        RenderNodeOperation atScale = BuildEllipseOp(outputScale);

        // One logical point inside the ellipse, one outside; both must agree across scales.
        var inside = new Point(50, 40);
        var outside = new Point(2, 2);

        Assert.Multiple(() =>
        {
            Assert.That(atScale.HitTest(inside), Is.EqualTo(atOne.HitTest(inside)), "inside-point parity");
            Assert.That(atScale.HitTest(outside), Is.EqualTo(atOne.HitTest(outside)), "outside-point parity");
            Assert.That(atScale.HitTest(inside), Is.True, "inside point should hit");
            Assert.That(atScale.HitTest(outside), Is.False, "outside point should miss");
        });

        atOne.Dispose();
        atScale.Dispose();
    }
}
