using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class OpacityMaskRenderNodeTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void HidesPoint_UsesSamplesFromACustomBrush(bool invert)
    {
        using var brush = new SamplingBrushResource { Opacity = 100f };
        var bounds = new Rect(10, 20, 20, 20);
        using var mask = new OpacityMaskRenderNode(brush, bounds, invert);

        Assert.Multiple(() =>
        {
            Assert.That(mask.HidesPoint(new Point(15, 25)), Is.EqualTo(!invert));
            Assert.That(mask.HidesPoint(new Point(25, 25)), Is.EqualTo(invert));
        });
    }

    [Test]
    public void HidesPoint_KeepsThePointEligibleWhenSamplingIsUnsupported()
    {
        using var brush = new DrawableBrush.Resource();
        using var mask = new OpacityMaskRenderNode(brush, new Rect(10, 20, 20, 20), invert: false);

        Assert.That(mask.HidesPoint(new Point(15, 25)), Is.False);
    }

    private sealed class SamplingBrushResource : Brush.Resource
    {
        public override bool TrySampleColor(Rect bounds, Point point, out Color color)
        {
            byte alpha = point.X < bounds.Center.X ? (byte)127 : (byte)128;
            color = Color.FromArgb(alpha, 255, 255, 255);
            return true;
        }
    }
}
