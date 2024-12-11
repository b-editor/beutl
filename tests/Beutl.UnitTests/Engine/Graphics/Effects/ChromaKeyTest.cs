
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class ChromaKeyTest
{
    [Test]
    public void ChromaKey_ShouldHaveDefaultValues()
    {
        var chromaKey = new ChromaKey();

        Assert.That(chromaKey.Color, Is.EqualTo((Color)default));
        Assert.That(chromaKey.HueRange, Is.EqualTo(0));
        Assert.That(chromaKey.SaturationRange, Is.EqualTo(0));
    }

    [Test]
    public void ChromaKey_ShouldUpdateProperties()
    {
        var chromaKey = new ChromaKey();
        var newColor = Colors.Green;
        var newHueRange = 10f;
        var newSaturationRange = 20f;

        chromaKey.Color = newColor;
        chromaKey.HueRange = newHueRange;
        chromaKey.SaturationRange = newSaturationRange;

        Assert.That(chromaKey.Color, Is.EqualTo(newColor));
        Assert.That(chromaKey.HueRange, Is.EqualTo(newHueRange));
        Assert.That(chromaKey.SaturationRange, Is.EqualTo(newSaturationRange));
    }

    [Test]
    public void ChromaKey_ShouldApplyToContext()
    {
        var chromaKey = new ChromaKey();
        var color = Colors.Green;
        var hueRange = 10f;
        var saturationRange = 20f;
        chromaKey.Color = color;
        chromaKey.HueRange = hueRange;
        chromaKey.SaturationRange = saturationRange;
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(chromaKey);

        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(chromaKey));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Custom>());
    }
}