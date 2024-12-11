using Beutl.Graphics;
using Beutl.Graphics.Effects;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class BlurTest
{
    [Test]
    public void Blur_ShouldHaveDefaultValues()
    {
        var blur = new Blur();

        Assert.That(blur.Sigma, Is.EqualTo(Size.Empty));
    }

    [Test]
    public void Blur_ShouldUpdateSigmaProperty()
    {
        var blur = new Blur();
        var newSigma = new Size(5, 5);

        blur.Sigma = newSigma;

        Assert.That(blur.Sigma, Is.EqualTo(newSigma));
    }

    [Test]
    public void Blur_ShouldApplyToContext()
    {
        var blur = new Blur();
        var sigma = new Size(5, 5);
        blur.Sigma = sigma;
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(blur);

        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(blur));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Skia>());
    }

    [Test]
    public void Blur_ShouldTransformBounds()
    {
        var blur = new Blur();
        var sigma = new Size(5, 5);
        blur.Sigma = sigma;
        var bounds = new Rect(0, 0, 100, 100);

        var transformedBounds = blur.TransformBounds(bounds);

        Assert.That(transformedBounds, Is.EqualTo(bounds.Inflate(new Thickness(sigma.Width * 3, sigma.Height * 3))));
    }
}