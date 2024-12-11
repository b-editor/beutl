
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class DropShadowTest
{
    [Test]
    public void DropShadow_ShouldHaveDefaultValues()
    {
        var dropShadow = new DropShadow();

        Assert.That(dropShadow.Position, Is.EqualTo(new Point()));
        Assert.That(dropShadow.Sigma, Is.EqualTo(Size.Empty));
        Assert.That(dropShadow.Color, Is.EqualTo((Color)default));
        Assert.That(dropShadow.ShadowOnly, Is.False);
    }

    [Test]
    public void DropShadow_ShouldUpdateProperties()
    {
        var dropShadow = new DropShadow();
        dropShadow.Position = new Point(10, 10);
        dropShadow.Sigma = new Size(5, 5);
        dropShadow.Color = Colors.Black;
        dropShadow.ShadowOnly = true;

        Assert.That(dropShadow.Position, Is.EqualTo(new Point(10, 10)));
        Assert.That(dropShadow.Sigma, Is.EqualTo(new Size(5, 5)));
        Assert.That(dropShadow.Color, Is.EqualTo(Colors.Black));
        Assert.That(dropShadow.ShadowOnly, Is.True);
    }

    [Test]
    public void DropShadow_ShouldApplyToContext()
    {
        var dropShadow = new DropShadow();
        dropShadow.Position = new Point(10, 10);
        dropShadow.Sigma = new Size(5, 5);
        dropShadow.Color = Colors.Black;
        dropShadow.ShadowOnly = true;
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(dropShadow);

        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(dropShadow));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Skia>());
    }

    [Test]
    public void DropShadow_TransformBounds_ShouldInflateBounds()
    {
        var dropShadow = new DropShadow();
        dropShadow.Position = new Point(10, 10);
        dropShadow.Sigma = new Size(5, 5);
        dropShadow.ShadowOnly = false;

        var originalBounds = new Rect(0, 0, 100, 100);
        var transformedBounds = dropShadow.TransformBounds(originalBounds);

        var expectedBounds = originalBounds
            .Translate(dropShadow.Position)
            .Inflate(new Thickness(dropShadow.Sigma.Width * 3, dropShadow.Sigma.Height * 3))
            .Union(originalBounds);

        Assert.That(transformedBounds, Is.EqualTo(expectedBounds));
    }

    [Test]
    public void DropShadow_TransformBounds_ShouldReturnShadowBoundsWhenShadowOnly()
    {
        var dropShadow = new DropShadow();
        dropShadow.Position = new Point(10, 10);
        dropShadow.Sigma = new Size(5, 5);
        dropShadow.ShadowOnly = true;

        var originalBounds = new Rect(0, 0, 100, 100);
        var transformedBounds = dropShadow.TransformBounds(originalBounds);

        var expectedBounds = originalBounds
            .Translate(dropShadow.Position)
            .Inflate(new Thickness(dropShadow.Sigma.Width * 3, dropShadow.Sigma.Height * 3));

        Assert.That(transformedBounds, Is.EqualTo(expectedBounds));
    }
}