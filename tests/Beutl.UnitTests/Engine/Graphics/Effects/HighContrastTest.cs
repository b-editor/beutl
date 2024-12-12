
using Beutl.Graphics;
using Beutl.Graphics.Effects;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class HighContrastTest
{
    [Test]
    public void HighContrast_ShouldHaveDefaultValues()
    {
        var highContrast = new HighContrast();

        Assert.That(highContrast.Grayscale, Is.False);
        Assert.That(highContrast.InvertStyle, Is.EqualTo(HighContrastInvertStyle.NoInvert));
        Assert.That(highContrast.Contrast, Is.EqualTo(0));
    }

    [Test]
    public void HighContrast_ShouldUpdateProperties()
    {
        var highContrast = new HighContrast();
        highContrast.Grayscale = true;
        highContrast.InvertStyle = HighContrastInvertStyle.InvertLightness;
        highContrast.Contrast = 50;

        Assert.That(highContrast.Grayscale, Is.True);
        Assert.That(highContrast.InvertStyle, Is.EqualTo(HighContrastInvertStyle.InvertLightness));
        Assert.That(highContrast.Contrast, Is.EqualTo(50));
    }

    [Test]
    public void HighContrast_ShouldApplyToContext()
    {
        var highContrast = new HighContrast();
        highContrast.Grayscale = true;
        highContrast.InvertStyle = HighContrastInvertStyle.InvertBrightness;
        highContrast.Contrast = 50;
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(highContrast);

        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(highContrast));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Skia>());
    }
}
