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
            Assert.That(pen.Brush.CurrentValue, Is.Null);
            Assert.That(pen.DashArray.CurrentValue, Is.Null);
            Assert.That(pen.DashOffset.CurrentValue, Is.EqualTo(0f));
            Assert.That(pen.Thickness.CurrentValue, Is.EqualTo(1f));
            Assert.That(pen.MiterLimit.CurrentValue, Is.EqualTo(10f));
            Assert.That(pen.StrokeCap.CurrentValue, Is.EqualTo(StrokeCap.Flat));
            Assert.That(pen.StrokeJoin.CurrentValue, Is.EqualTo(StrokeJoin.Miter));
            Assert.That(pen.StrokeAlignment.CurrentValue, Is.EqualTo(StrokeAlignment.Center));
            Assert.That(pen.TrimStart.CurrentValue, Is.EqualTo(0f));
            Assert.That(pen.TrimEnd.CurrentValue, Is.EqualTo(100f));
            Assert.That(pen.TrimOffset.CurrentValue, Is.EqualTo(0f));
            Assert.That(pen.Offset.CurrentValue, Is.EqualTo(0f));
        });
    }

    [Test]
    public void Pen_SettableValues_AreReflectedInCurrentValue()
    {
        var pen = new Pen();
        var brush = new SolidColorBrush(Colors.Red);

        pen.Brush.CurrentValue = brush;
        pen.Thickness.CurrentValue = 5f;
        pen.StrokeCap.CurrentValue = StrokeCap.Round;
        pen.StrokeJoin.CurrentValue = StrokeJoin.Bevel;

        Assert.Multiple(() =>
        {
            Assert.That(pen.Brush.CurrentValue, Is.SameAs(brush));
            Assert.That(pen.Thickness.CurrentValue, Is.EqualTo(5f));
            Assert.That(pen.StrokeCap.CurrentValue, Is.EqualTo(StrokeCap.Round));
            Assert.That(pen.StrokeJoin.CurrentValue, Is.EqualTo(StrokeJoin.Bevel));
        });
    }

    [Test]
    public void GradientStop_DefaultConstructor_HasDefaultValues()
    {
        var stop = new GradientStop();
        Assert.Multiple(() =>
        {
            Assert.That(stop.Offset.CurrentValue, Is.EqualTo(0f));
            Assert.That(stop.Color.CurrentValue, Is.EqualTo(default(Color)));
        });
    }

    [Test]
    public void GradientStop_ColorOffsetConstructor_StoresValues()
    {
        var color = Color.FromArgb(0xFF, 0x12, 0x34, 0x56);
        var stop = new GradientStop(color, offset: 0.5f);

        Assert.Multiple(() =>
        {
            Assert.That(stop.Color.CurrentValue, Is.EqualTo(color));
            Assert.That(stop.Offset.CurrentValue, Is.EqualTo(0.5f));
        });
    }

    [Test]
    public void GradientStop_PropertyAssignment_PersistsValues()
    {
        var stop = new GradientStop();
        stop.Offset.CurrentValue = 0.75f;
        stop.Color.CurrentValue = Colors.Blue;

        Assert.Multiple(() =>
        {
            Assert.That(stop.Offset.CurrentValue, Is.EqualTo(0.75f));
            Assert.That(stop.Color.CurrentValue, Is.EqualTo(Colors.Blue));
        });
    }
}
