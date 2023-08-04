using Beutl.Animation;
using Beutl.Graphics.Transformation;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

public sealed class TransformEffect : FilterEffect
{
    public static readonly CoreProperty<ITransform?> TransformProperty;
    public static readonly CoreProperty<BitmapInterpolationMode> BitmapInterpolationModeProperty;
    private ITransform? _transform;
    private BitmapInterpolationMode _interpolationMode;

    static TransformEffect()
    {
        TransformProperty = ConfigureProperty<ITransform?, TransformEffect>(nameof(Transform))
            .Accessor(o => o.Transform, (o, v) => o.Transform = v)
            .DefaultValue(null)
            .Register();

        BitmapInterpolationModeProperty = ConfigureProperty<BitmapInterpolationMode, TransformEffect>(nameof(BitmapInterpolationMode))
            .Accessor(o => o.BitmapInterpolationMode, (o, v) => o.BitmapInterpolationMode = v)
            .DefaultValue(Media.BitmapInterpolationMode.Default)
            .Register();

        AffectsRender<TransformEffect>(TransformProperty, BitmapInterpolationModeProperty);
    }

    public ITransform? Transform
    {
        get => _transform;
        set => SetAndRaise(TransformProperty, ref _transform, value);
    }

    public BitmapInterpolationMode BitmapInterpolationMode
    {
        get => _interpolationMode;
        set => SetAndRaise(BitmapInterpolationModeProperty, ref _interpolationMode, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        if (_transform is { IsEnabled: true, Value: Matrix mat })
        {
            context.Transform(mat, BitmapInterpolationMode);
        }
    }

    public override Rect TransformBounds(Rect bounds)
    {
        if (_transform is { IsEnabled: true, Value: Matrix mat })
        {
            return bounds.TransformToAABB(mat);
        }

        return bounds;
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        (Transform as IAnimatable)?.ApplyAnimations(clock);
    }
}
