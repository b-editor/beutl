
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class InvertTest
{
    [Test]
    public void Invert_ShouldHaveDefaultValues()
    {
        var invert = new Invert();

        Assert.That(invert.Amount, Is.EqualTo(100));
        Assert.That(invert.ExcludeAlphaChannel, Is.True);
    }

    [Test]
    public void Invert_ShouldUpdateProperties()
    {
        var invert = new Invert();
        invert.Amount = 50;
        invert.ExcludeAlphaChannel = false;

        Assert.That(invert.Amount, Is.EqualTo(50));
        Assert.That(invert.ExcludeAlphaChannel, Is.False);
    }

    [Test]
    public void Invert_ShouldApplyToContext()
    {
        var invert = new Invert();
        invert.Amount = 50;
        invert.ExcludeAlphaChannel = false;
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(invert);

        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(invert));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Skia>());
    }
}