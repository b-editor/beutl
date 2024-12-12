
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class InnerShadowTest
{
    [Test]
    public void InnerShadow_ShouldHaveDefaultValues()
    {
        var innerShadow = new InnerShadow();

        Assert.That(innerShadow.Position, Is.EqualTo(new Point()));
        Assert.That(innerShadow.Sigma, Is.EqualTo(Size.Empty));
        Assert.That(innerShadow.Color, Is.EqualTo((Color)default));
        Assert.That(innerShadow.ShadowOnly, Is.False);
    }

    [Test]
    public void InnerShadow_ShouldUpdateProperties()
    {
        var innerShadow = new InnerShadow();
        innerShadow.Position = new Point(10, 10);
        innerShadow.Sigma = new Size(5, 5);
        innerShadow.Color = Colors.Black;
        innerShadow.ShadowOnly = true;

        Assert.That(innerShadow.Position, Is.EqualTo(new Point(10, 10)));
        Assert.That(innerShadow.Sigma, Is.EqualTo(new Size(5, 5)));
        Assert.That(innerShadow.Color, Is.EqualTo(Colors.Black));
        Assert.That(innerShadow.ShadowOnly, Is.True);
    }

    [Test]
    public void InnerShadow_ShouldApplyToContext()
    {
        var innerShadow = new InnerShadow();
        innerShadow.Position = new Point(10, 10);
        innerShadow.Sigma = new Size(5, 5);
        innerShadow.Color = Colors.Black;
        innerShadow.ShadowOnly = true;
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(innerShadow);

        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(innerShadow));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Custom>());
    }
}
