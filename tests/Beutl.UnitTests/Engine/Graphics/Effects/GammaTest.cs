
using Beutl.Graphics;
using Beutl.Graphics.Effects;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class GammaTest
{
    [Test]
    public void Gamma_ShouldHaveDefaultValues()
    {
        var gamma = new Gamma();

        Assert.That(gamma.Amount, Is.EqualTo(100));
        Assert.That(gamma.Strength, Is.EqualTo(100));
    }

    [Test]
    public void Gamma_ShouldUpdateProperties()
    {
        var gamma = new Gamma();
        gamma.Amount = 150;
        gamma.Strength = 80;

        Assert.That(gamma.Amount, Is.EqualTo(150));
        Assert.That(gamma.Strength, Is.EqualTo(80));
    }

    [Test]
    public void Gamma_ShouldApplyToContext()
    {
        var gamma = new Gamma();
        gamma.Amount = 150;
        gamma.Strength = 80;
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(gamma);

        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(gamma));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Skia>());
    }
}