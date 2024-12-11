
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class MosaicTest
{
    [Test]
    public void Mosaic_ShouldHaveDefaultValues()
    {
        var mosaic = new Mosaic();

        Assert.That(mosaic.Scale, Is.EqualTo(1));
        Assert.That(mosaic.ScaleX, Is.EqualTo(1));
        Assert.That(mosaic.ScaleY, Is.EqualTo(1));
    }

    [Test]
    public void Mosaic_ShouldUpdateProperties()
    {
        var mosaic = new Mosaic();
        mosaic.Scale = 2;
        mosaic.ScaleX = 1.5f;
        mosaic.ScaleY = 1.5f;

        Assert.That(mosaic.Scale, Is.EqualTo(2));
        Assert.That(mosaic.ScaleX, Is.EqualTo(1.5f));
        Assert.That(mosaic.ScaleY, Is.EqualTo(1.5f));
    }

    [Test]
    public void Mosaic_ShouldApplyToContext()
    {
        var mosaic = new Mosaic();
        mosaic.Scale = 2;
        mosaic.ScaleX = 1.5f;
        mosaic.ScaleY = 1.5f;
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(mosaic);

        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(mosaic));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Custom>());
    }
}