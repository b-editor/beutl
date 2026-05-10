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
            Assert.That(brush.Color.CurrentValue, Is.EqualTo(default(Color)));
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

    [Test]
    public void UInt32Constructor_DecodesArgb()
    {
        // 0xFFAABBCC = ARGB(0xFF, 0xAA, 0xBB, 0xCC)
        var brush = new SolidColorBrush(0xFFAABBCCu);
        Color c = brush.Color.CurrentValue;

        Assert.Multiple(() =>
        {
            Assert.That(c.A, Is.EqualTo(0xFF));
            Assert.That(c.R, Is.EqualTo(0xAA));
            Assert.That(c.G, Is.EqualTo(0xBB));
            Assert.That(c.B, Is.EqualTo(0xCC));
        });
    }

    [Test]
    public void ColorExtension_ToBrush_WrapsColorInSolidBrush()
    {
        var c = Color.FromArgb(0x80, 0x11, 0x22, 0x33);
        SolidColorBrush brush = c.ToBrush();

        Assert.That(brush.Color.CurrentValue, Is.EqualTo(c));
    }
}
