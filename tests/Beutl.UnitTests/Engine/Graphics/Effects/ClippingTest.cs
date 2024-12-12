using Beutl.Graphics;
using Beutl.Graphics.Effects;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class ClippingTest
{
    [Test]
    public void Clipping_ShouldHaveDefaultValues()
    {
        var clipping = new Clipping();

        Assert.That(clipping.Left, Is.EqualTo(0));
        Assert.That(clipping.Top, Is.EqualTo(0));
        Assert.That(clipping.Right, Is.EqualTo(0));
        Assert.That(clipping.Bottom, Is.EqualTo(0));
        Assert.That(clipping.AutoCenter, Is.False);
        Assert.That(clipping.AutoClip, Is.False);
    }

    [Test]
    public void Clipping_ShouldUpdateProperties()
    {
        var clipping = new Clipping();
        var newLeft = 10f;
        var newTop = 10f;
        var newRight = 10f;
        var newBottom = 10f;
        var newAutoCenter = true;
        var newAutoClip = true;

        clipping.Left = newLeft;
        clipping.Top = newTop;
        clipping.Right = newRight;
        clipping.Bottom = newBottom;
        clipping.AutoCenter = newAutoCenter;
        clipping.AutoClip = newAutoClip;

        Assert.That(clipping.Left, Is.EqualTo(newLeft));
        Assert.That(clipping.Top, Is.EqualTo(newTop));
        Assert.That(clipping.Right, Is.EqualTo(newRight));
        Assert.That(clipping.Bottom, Is.EqualTo(newBottom));
        Assert.That(clipping.AutoCenter, Is.EqualTo(newAutoCenter));
        Assert.That(clipping.AutoClip, Is.EqualTo(newAutoClip));
    }

    [Test]
    public void Clipping_ShouldApplyToContext()
    {
        var clipping = new Clipping
        {
            Left = 10f,
            Top = 10f,
            Right = 10f,
            Bottom = 10f
        };
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(clipping);

        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(clipping));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Custom>());
    }

    [Test]
    public void Clipping_TransformBounds_ShouldReturnCorrectBounds()
    {
        var clipping = new Clipping
        {
            Left = 10f,
            Top = 10f,
            Right = 10f,
            Bottom = 10f
        };
        var originalBounds = new Rect(0, 0, 100, 100);
        var expectedBounds = new Rect(10, 10, 80, 80);

        var transformedBounds = clipping.TransformBounds(originalBounds);

        Assert.That(transformedBounds, Is.EqualTo(expectedBounds));
    }

    [Test]
    public void Clipping_TransformBounds_WithAutoCenter_ShouldReturnCenteredBounds()
    {
        var clipping = new Clipping
        {
            Left = 10f,
            Top = 10f,
            Right = 10f,
            Bottom = 10f,
            AutoCenter = true
        };
        var originalBounds = new Rect(0, 0, 100, 100);
        var expectedBounds = originalBounds.CenterRect(new Rect(10, 10, 80, 80));

        var transformedBounds = clipping.TransformBounds(originalBounds);

        Assert.That(transformedBounds, Is.EqualTo(expectedBounds));
    }

    [Test]
    public void Clipping_TransformBounds_WithAutoClip_ShouldReturnInvalidBounds()
    {
        var clipping = new Clipping
        {
            Left = 10f,
            Top = 10f,
            Right = 10f,
            Bottom = 10f,
            AutoClip = true
        };
        var originalBounds = new Rect(0, 0, 100, 100);

        var transformedBounds = clipping.TransformBounds(originalBounds);

        Assert.That(transformedBounds.IsInvalid, Is.True);
    }
}
