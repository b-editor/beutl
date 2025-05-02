
using Beutl.Graphics;
using Beutl.Graphics.Effects;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class HueRotateTest
{
    [Test]
    public void HueRotate_ShouldHaveDefaultValues()
    {
        var hueRotate = new HueRotate();

        Assert.That(hueRotate.Angle, Is.EqualTo(0));
    }

    [Test]
    public void HueRotate_ShouldUpdateProperties()
    {
        var hueRotate = new HueRotate();
        hueRotate.Angle = 180;

        Assert.That(hueRotate.Angle, Is.EqualTo(180));
    }

    [Test]
    public void HueRotate_ShouldApplyToContext()
    {
        var hueRotate = new HueRotate();
        hueRotate.Angle = 180;
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(hueRotate);

        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(hueRotate));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Skia>());
    }
}
