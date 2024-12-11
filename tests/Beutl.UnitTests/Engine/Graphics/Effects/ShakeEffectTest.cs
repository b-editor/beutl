using Beutl.Graphics;
using Beutl.Graphics.Effects;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class ShakeEffectTest
{
    [Test]
    public void ShakeEffect_ShouldHaveDefaultValues()
    {
        var effect = new ShakeEffect();

        Assert.That(effect.StrengthX, Is.EqualTo(50));
        Assert.That(effect.StrengthY, Is.EqualTo(50));
        Assert.That(effect.Speed, Is.EqualTo(100));
    }

    [Test]
    public void ShakeEffect_ShouldUpdateProperties()
    {
        var effect = new ShakeEffect();
        effect.StrengthX = 75;
        effect.StrengthY = 25;
        effect.Speed = 50;

        Assert.That(effect.StrengthX, Is.EqualTo(75));
        Assert.That(effect.StrengthY, Is.EqualTo(25));
        Assert.That(effect.Speed, Is.EqualTo(50));
    }

    [Test]
    public void ShakeEffect_ShouldApplyToContext()
    {
        var effect = new ShakeEffect();
        effect.StrengthX = 75;
        effect.StrengthY = 25;
        effect.Speed = 50;
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(effect);

        // 適用結果の検証
        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(effect));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Custom>());
    }
}