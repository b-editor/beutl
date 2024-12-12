
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class NegaposiTest
{
    [Test]
    public void Negaposi_ShouldHaveDefaultValues()
    {
        var negaposi = new Negaposi();

        Assert.That(negaposi.Red, Is.EqualTo(0));
        Assert.That(negaposi.Green, Is.EqualTo(0));
        Assert.That(negaposi.Blue, Is.EqualTo(0));
        Assert.That(negaposi.Strength, Is.EqualTo(100));
    }

    [Test]
    public void Negaposi_ShouldUpdateProperties()
    {
        var negaposi = new Negaposi();
        negaposi.Red = 255;
        negaposi.Green = 128;
        negaposi.Blue = 64;
        negaposi.Strength = 50;

        Assert.That(negaposi.Red, Is.EqualTo(255));
        Assert.That(negaposi.Green, Is.EqualTo(128));
        Assert.That(negaposi.Blue, Is.EqualTo(64));
        Assert.That(negaposi.Strength, Is.EqualTo(50));
    }

    [Test]
    public void Negaposi_ShouldApplyToContext()
    {
        var negaposi = new Negaposi();
        negaposi.Red = 255;
        negaposi.Green = 128;
        negaposi.Blue = 64;
        negaposi.Strength = 50;
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(negaposi);

        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(negaposi));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Skia>());
    }
}
