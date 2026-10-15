using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics;

public class SolidColorBrushTests
{
    [Test]
    public void DefaultConstructor_HasDefaultColorAndOpacity()
    {
        var brush = new SolidColorBrush();
        Assert.Multiple(() =>
        {
            Assert.That(brush.Color.CurrentValue, Is.EqualTo(Colors.White));
            Assert.That(brush.Opacity.CurrentValue, Is.EqualTo(100f));
        });
    }

    [Test]
    public void ColorConstructor_StoresColor()
    {
        var c = Color.FromArgb(0xFF, 0x10, 0x20, 0x30);
        var brush = new SolidColorBrush(c);

        Assert.Multiple(() =>
        {
            Assert.That(brush.Color.CurrentValue, Is.EqualTo(c));
            Assert.That(brush.Opacity.CurrentValue, Is.EqualTo(100f));
        });
    }

    [Test]
    public void ColorOpacityConstructor_StoresBoth()
    {
        var c = Colors.Red;
        var brush = new SolidColorBrush(c, opacity: 50f);

        Assert.Multiple(() =>
        {
            Assert.That(brush.Color.CurrentValue, Is.EqualTo(c));
            Assert.That(brush.Opacity.CurrentValue, Is.EqualTo(50f));
        });
    }
}
