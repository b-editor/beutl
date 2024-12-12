using Beutl.Graphics;
using Beutl.Graphics.Effects;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class SplitEffectTest
{
    [Test]
    public void SplitEffect_ShouldHaveDefaultValues()
    {
        var effect = new SplitEffect();

        Assert.That(effect.HorizontalDivisions, Is.EqualTo(2));
        Assert.That(effect.VerticalDivisions, Is.EqualTo(2));
        Assert.That(effect.HorizontalSpacing, Is.EqualTo(0));
        Assert.That(effect.VerticalSpacing, Is.EqualTo(0));
    }

    [Test]
    public void SplitEffect_ShouldUpdateProperties()
    {
        var effect = new SplitEffect();
        effect.HorizontalDivisions = 3;
        effect.VerticalDivisions = 4;
        effect.HorizontalSpacing = 5;
        effect.VerticalSpacing = 6;

        Assert.That(effect.HorizontalDivisions, Is.EqualTo(3));
        Assert.That(effect.VerticalDivisions, Is.EqualTo(4));
        Assert.That(effect.HorizontalSpacing, Is.EqualTo(5));
        Assert.That(effect.VerticalSpacing, Is.EqualTo(6));
    }

    [Test]
    public void SplitEffect_ShouldApplyToContext()
    {
        var effect = new SplitEffect();
        effect.HorizontalDivisions = 3;
        effect.VerticalDivisions = 4;
        effect.HorizontalSpacing = 5;
        effect.VerticalSpacing = 6;
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(effect);

        // 適用結果の検証
        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(effect));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Custom>());
    }
}
