using System.ComponentModel.DataAnnotations;

using Beutl.Animation;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

public sealed class TransformEffect : FilterEffect
{
    public static readonly CoreProperty<ITransform?> TransformProperty;
    public static readonly CoreProperty<RelativePoint> TransformOriginProperty;
    public static readonly CoreProperty<BitmapInterpolationMode> BitmapInterpolationModeProperty;
    private ITransform? _transform;
    private RelativePoint _transformOrigin = RelativePoint.Center;
    private BitmapInterpolationMode _interpolationMode;

    static TransformEffect()
    {
        TransformProperty = ConfigureProperty<ITransform?, TransformEffect>(nameof(Transform))
            .Accessor(o => o.Transform, (o, v) => o.Transform = v)
            .DefaultValue(null)
            .Register();

        TransformOriginProperty = ConfigureProperty<RelativePoint, TransformEffect>(nameof(TransformOrigin))
            .Accessor(o => o.TransformOrigin, (o, v) => o.TransformOrigin = v)
            .DefaultValue(RelativePoint.Center)
            .Register();

        BitmapInterpolationModeProperty = ConfigureProperty<BitmapInterpolationMode, TransformEffect>(nameof(BitmapInterpolationMode))
            .Accessor(o => o.BitmapInterpolationMode, (o, v) => o.BitmapInterpolationMode = v)
            .DefaultValue(BitmapInterpolationMode.Default)
            .Register();

        AffectsRender<TransformEffect>(TransformProperty, TransformOriginProperty, BitmapInterpolationModeProperty);
    }

    [Display(Name = nameof(Strings.Transform), ResourceType = typeof(Strings))]
    public ITransform? Transform
    {
        get => _transform;
        set => SetAndRaise(TransformProperty, ref _transform, value);
    }

    [Display(Name = nameof(Strings.TransformOrigin), ResourceType = typeof(Strings))]
    public RelativePoint TransformOrigin
    {
        get => _transformOrigin;
        set => SetAndRaise(TransformOriginProperty, ref _transformOrigin, value);
    }

    [Display(Name = nameof(Strings.BitmapInterpolationMode), ResourceType = typeof(Strings))]
    public BitmapInterpolationMode BitmapInterpolationMode
    {
        get => _interpolationMode;
        set => SetAndRaise(BitmapInterpolationModeProperty, ref _interpolationMode, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        if (_transform is { IsEnabled: true, Value: Matrix mat })
        {
            Vector origin = TransformOrigin.ToPixels(context.Bounds.Size) + context.Bounds.Position;
            Matrix offset = Matrix.CreateTranslation(origin);

            mat = (-offset) * mat * offset;
            context.Transform(mat, BitmapInterpolationMode);
        }
    }

    public override Rect TransformBounds(Rect bounds)
    {
        Vector origin = TransformOrigin.ToPixels(bounds.Size) + bounds.Position;
        Matrix offset = Matrix.CreateTranslation(origin);

        if (_transform is { IsEnabled: true, Value: Matrix mat })
        {
            mat = (-offset) * mat * offset;
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
