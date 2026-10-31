using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics;

public class PenAndGradientStopTests
{
    [Test]
    public void Pen_DefaultValues()
    {
        var pen = new Pen();

        Assert.Multiple(() =>
        {
            Assert.That(pen.Brush.Value, Is.Null);
            Assert.That(pen.DashArray.Value, Is.Null);
            Assert.That(pen.DashOffset.Value, Is.EqualTo(0f));
            Assert.That(pen.Thickness.Value, Is.EqualTo(1f));
            Assert.That(pen.MiterLimit.Value, Is.EqualTo(10f));
            Assert.That(pen.StrokeCap.Value, Is.EqualTo(StrokeCap.Flat));
            Assert.That(pen.StrokeJoin.Value, Is.EqualTo(StrokeJoin.Miter));
            Assert.That(pen.StrokeAlignment.Value, Is.EqualTo(StrokeAlignment.Center));
            Assert.That(pen.StrokeOrder.Value, Is.EqualTo(StrokeOrder.AboveFill));
            Assert.That(pen.TrimStart.Value, Is.EqualTo(0f));
            Assert.That(pen.TrimEnd.Value, Is.EqualTo(100f));
            Assert.That(pen.TrimOffset.Value, Is.EqualTo(0f));
            Assert.That(pen.Offset.Value, Is.EqualTo(0f));
        });
    }

    [Test]
    public void GradientStop_DefaultConstructor_HasDefaultValues()
    {
        var stop = new GradientStop();
        Assert.Multiple(() =>
        {
            Assert.That(stop.Offset.Value, Is.EqualTo(0f));
            Assert.That(stop.Color.Value, Is.EqualTo(Colors.White));
        });
    }

    [Test]
    public void GradientStop_ColorOffsetConstructor_StoresValues()
    {
        var color = Color.FromArgb(0xFF, 0x12, 0x34, 0x56);
        var stop = new GradientStop(color, offset: 0.5f);

        Assert.Multiple(() =>
        {
            Assert.That(stop.Color.Value, Is.EqualTo(color));
            Assert.That(stop.Offset.Value, Is.EqualTo(0.5f));
        });
    }
}
