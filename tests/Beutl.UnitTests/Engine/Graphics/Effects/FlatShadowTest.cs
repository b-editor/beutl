
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class FlatShadowTest
{
    [Test]
    public void FlatShadow_ShouldHaveDefaultValues()
    {
        var flatShadow = new FlatShadow();

        Assert.That(flatShadow.Angle, Is.EqualTo(0));
        Assert.That(flatShadow.Length, Is.EqualTo(0));
        Assert.That(flatShadow.Brush, Is.InstanceOf<SolidColorBrush>());
        Assert.That(flatShadow.ShadowOnly, Is.False);
    }

    [Test]
    public void FlatShadow_ShouldUpdateProperties()
    {
        var flatShadow = new FlatShadow();
        flatShadow.Angle = 45;
        flatShadow.Length = 10;
        flatShadow.Brush = new SolidColorBrush(Colors.Red);
        flatShadow.ShadowOnly = true;

        Assert.That(flatShadow.Angle, Is.EqualTo(45));
        Assert.That(flatShadow.Length, Is.EqualTo(10));
        Assert.That(flatShadow.Brush, Is.InstanceOf<SolidColorBrush>());
        Assert.That(flatShadow.ShadowOnly, Is.True);
    }

    [Test]
    public void FlatShadow_ShouldApplyToContext()
    {
        var flatShadow = new FlatShadow();
        flatShadow.Angle = 45;
        flatShadow.Length = 10;
        flatShadow.Brush = new SolidColorBrush(Colors.Red);
        flatShadow.ShadowOnly = true;
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(flatShadow);

        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(flatShadow));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Custom>());
    }
}
