
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class DisplacementMapTest
{
    [Test]
    public void DisplacementMap_ShouldHaveDefaultValues()
    {
        var displacementMap = new DisplacementMap();

        Assert.That(displacementMap.XChannelSelector, Is.EqualTo(SKColorChannel.A));
        Assert.That(displacementMap.YChannelSelector, Is.EqualTo(SKColorChannel.A));
        Assert.That(displacementMap.Scale, Is.EqualTo(1f));
        Assert.That(displacementMap.Displacement, Is.Null);
    }

    [Test]
    public void DisplacementMap_ShouldUpdateProperties()
    {
        var displacementMap = new DisplacementMap();
        displacementMap.XChannelSelector = SKColorChannel.R;
        displacementMap.YChannelSelector = SKColorChannel.G;
        displacementMap.Scale = 2f;
        displacementMap.Displacement = new Blur();

        Assert.That(displacementMap.XChannelSelector, Is.EqualTo(SKColorChannel.R));
        Assert.That(displacementMap.YChannelSelector, Is.EqualTo(SKColorChannel.G));
        Assert.That(displacementMap.Scale, Is.EqualTo(2f));
        Assert.That(displacementMap.Displacement, Is.InstanceOf<Blur>());
    }

    [Test]
    public void DisplacementMap_ShouldApplyToContext()
    {
        var displacementMap = new DisplacementMap();
        displacementMap.XChannelSelector = SKColorChannel.R;
        displacementMap.YChannelSelector = SKColorChannel.G;
        displacementMap.Scale = 2f;
        displacementMap.Displacement = new Blur();
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(displacementMap);

        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(displacementMap));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Skia>());
    }
}
