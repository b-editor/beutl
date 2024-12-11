
using Beutl.Graphics;
using Beutl.Graphics.Effects;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class BrightnessTest
{
    [Test]
    public void Brightness_ShouldHaveDefaultValues()
    {
        var brightness = new Brightness();

        Assert.That(brightness.Amount, Is.EqualTo(100));
    }

    [Test]
    public void Brightness_ShouldUpdateAmountProperty()
    {
        var brightness = new Brightness();
        var newAmount = 150f;

        brightness.Amount = newAmount;

        Assert.That(brightness.Amount, Is.EqualTo(newAmount));
    }

    [Test]
    public void Brightness_ShouldApplyToContext()
    {
        var brightness = new Brightness();
        var amount = 150f;
        brightness.Amount = amount;
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(brightness);

        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(brightness));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Skia>());
    }
}