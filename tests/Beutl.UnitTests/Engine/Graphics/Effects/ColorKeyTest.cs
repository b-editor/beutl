using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class ColorKeyTest
{
    [Test]
    public void ColorKey_ShouldHaveDefaultValues()
    {
        var colorKey = new ColorKey();

        Assert.That(colorKey.Color, Is.EqualTo((Color)default));
        Assert.That(colorKey.Range, Is.EqualTo(0));
    }

    [Test]
    public void ColorKey_ShouldUpdateProperties()
    {
        var colorKey = new ColorKey();
        colorKey.Color = Colors.Red;
        colorKey.Range = 50;

        Assert.That(colorKey.Color, Is.EqualTo(Colors.Red));
        Assert.That(colorKey.Range, Is.EqualTo(50));
    }

    [Test]
    public void ColorKey_ShouldApplyToContext()
    {
        var colorKey = new ColorKey();
        colorKey.Color = Colors.Red;
        colorKey.Range = 50;
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(colorKey);

        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(colorKey));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Custom>());
    }
}