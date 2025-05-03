
using Beutl.Graphics;
using Beutl.Graphics.Effects;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class LumaColorTest
{
    [Test]
    public void LumaColor_ShouldApplyToContext()
    {
        var lumaColor = new LumaColor();
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(lumaColor);

        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(lumaColor));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Skia>());
    }
}
