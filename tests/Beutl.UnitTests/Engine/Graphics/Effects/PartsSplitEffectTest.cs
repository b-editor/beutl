
using Beutl.Graphics;
using Beutl.Graphics.Effects;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class PartsSplitEffectTest
{
    [Test]
    public void PartsSplitEffect_ShouldApplyToContext()
    {
        var effect = new PartsSplitEffect();
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(effect);

        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(effect));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Custom>());
    }
}
