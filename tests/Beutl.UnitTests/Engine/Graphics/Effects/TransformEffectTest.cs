
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class TransformEffectTest
{
    [Test]
    public void TransformEffect_ShouldHaveDefaultValues()
    {
        var effect = new TransformEffect();

        Assert.That(effect.Transform, Is.Null);
        Assert.That(effect.TransformOrigin, Is.EqualTo(RelativePoint.Center));
        Assert.That(effect.BitmapInterpolationMode, Is.EqualTo(BitmapInterpolationMode.Default));
        Assert.That(effect.ApplyToTarget, Is.False);
    }

    [Test]
    public void TransformEffect_ShouldUpdateProperties()
    {
        var effect = new TransformEffect();
        effect.Transform = new RotationTransform { Rotation = 45 };
        effect.TransformOrigin = RelativePoint.TopLeft;
        effect.BitmapInterpolationMode = BitmapInterpolationMode.HighQuality;
        effect.ApplyToTarget = false;

        Assert.That(effect.Transform, Is.Not.Null);
        Assert.That(effect.TransformOrigin, Is.EqualTo(RelativePoint.TopLeft));
        Assert.That(effect.BitmapInterpolationMode, Is.EqualTo(BitmapInterpolationMode.HighQuality));
        Assert.That(effect.ApplyToTarget, Is.False);
    }

    [Test]
    public void TransformEffect_ShouldApplyToContext()
    {
        var effect = new TransformEffect();
        effect.Transform = new RotationTransform { Rotation = 45 };
        effect.TransformOrigin = RelativePoint.TopLeft;
        effect.BitmapInterpolationMode = BitmapInterpolationMode.HighQuality;
        effect.ApplyToTarget = false;
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(effect);

        // 適用結果の検証
        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(effect));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Skia>());
    }
}
