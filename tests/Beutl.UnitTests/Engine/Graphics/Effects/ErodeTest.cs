
using Beutl.Graphics;
using Beutl.Graphics.Effects;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class ErodeTest
{
    [Test]
    public void Erode_ShouldHaveDefaultValues()
    {
        var erode = new Erode();

        Assert.That(erode.RadiusX, Is.EqualTo(0));
        Assert.That(erode.RadiusY, Is.EqualTo(0));
    }

    [Test]
    public void Erode_ShouldUpdateProperties()
    {
        var erode = new Erode();
        erode.RadiusX = 5;
        erode.RadiusY = 5;

        Assert.That(erode.RadiusX, Is.EqualTo(5));
        Assert.That(erode.RadiusY, Is.EqualTo(5));
    }

    [Test]
    public void Erode_ShouldApplyToContext()
    {
        var erode = new Erode();
        erode.RadiusX = 5;
        erode.RadiusY = 5;
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(erode);

        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(erode));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Skia>());
    }
}
