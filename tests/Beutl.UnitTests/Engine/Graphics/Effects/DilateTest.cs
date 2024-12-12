
using Beutl.Graphics;
using Beutl.Graphics.Effects;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class DilateTest
{
    [Test]
    public void Dilate_ShouldHaveDefaultValues()
    {
        var dilate = new Dilate();

        Assert.That(dilate.RadiusX, Is.EqualTo(0));
        Assert.That(dilate.RadiusY, Is.EqualTo(0));
    }

    [Test]
    public void Dilate_ShouldUpdateProperties()
    {
        var dilate = new Dilate();
        dilate.RadiusX = 5;
        dilate.RadiusY = 5;

        Assert.That(dilate.RadiusX, Is.EqualTo(5));
        Assert.That(dilate.RadiusY, Is.EqualTo(5));
    }

    [Test]
    public void Dilate_ShouldApplyToContext()
    {
        var dilate = new Dilate();
        dilate.RadiusX = 5;
        dilate.RadiusY = 5;
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(dilate);

        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(dilate));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Skia>());
    }
}
