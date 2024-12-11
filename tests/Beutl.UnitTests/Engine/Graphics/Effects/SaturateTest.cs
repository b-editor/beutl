
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class SaturateTest
{
    [Test]
    public void Saturate_ShouldHaveDefaultValues()
    {
        var saturate = new Saturate();

        Assert.That(saturate.Amount, Is.EqualTo(100F));
    }

    [Test]
    public void Saturate_ShouldUpdateProperties()
    {
        var saturate = new Saturate();
        saturate.Amount = 75F;

        Assert.That(saturate.Amount, Is.EqualTo(75F));
    }

    [Test]
    public void Saturate_ShouldApplyToContext()
    {
        var saturate = new Saturate();
        saturate.Amount = 75F;
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(saturate);

        // 適用結果の検証
        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(saturate));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Skia>());
    }
}