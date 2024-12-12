using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class BlendEffectTest
{
    [Test]
    public void BlendEffect_ShouldHaveDefaultValues()
    {
        var blendEffect = new BlendEffect();

        Assert.That(blendEffect.Brush, Is.Not.Null);
        Assert.That(blendEffect.Brush, Is.TypeOf<SolidColorBrush>());
        Assert.That(((SolidColorBrush)blendEffect.Brush!).Color, Is.EqualTo(Colors.White));
        Assert.That(blendEffect.BlendMode, Is.EqualTo(BlendMode.SrcIn));
    }

    [Test]
    public void BlendEffect_ShouldUpdateBrushProperty()
    {
        var blendEffect = new BlendEffect();
        var newBrush = new SolidColorBrush(Colors.Red);

        blendEffect.Brush = newBrush;

        Assert.That(blendEffect.Brush, Is.EqualTo(newBrush));
    }

    [Test]
    public void BlendEffect_ShouldUpdateBlendModeProperty()
    {
        var blendEffect = new BlendEffect();
        var newBlendMode = BlendMode.Multiply;

        blendEffect.BlendMode = newBlendMode;

        Assert.That(blendEffect.BlendMode, Is.EqualTo(newBlendMode));
    }

    [Test]
    public void BlendEffect_ShouldApplyToContext()
    {
        var blendEffect = new BlendEffect();
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(blendEffect);

        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(blendEffect));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Custom>());
    }

    [Test]
    public void BlendEffect_ShouldApplyAnimations()
    {
        var brush = new Mock<IBrush>();
        brush.As<IAnimatable>().Setup(b => b.ApplyAnimations(It.IsAny<IClock>()));
        var blendEffect = new BlendEffect()
        {
            Brush = brush.Object
        };
        var clock = new ZeroClock();

        blendEffect.ApplyAnimations(clock);

        brush.As<IAnimatable>().Verify(b => b.ApplyAnimations(clock), Times.Once);
    }
}
