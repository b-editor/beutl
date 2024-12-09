using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

// TODO: EffectTargetが複数の場合に対応する
public sealed class TransformEffect : FilterEffect
{
    public static readonly CoreProperty<ITransform?> TransformProperty;
    public static readonly CoreProperty<RelativePoint> TransformOriginProperty;
    public static readonly CoreProperty<BitmapInterpolationMode> BitmapInterpolationModeProperty;
    public static readonly CoreProperty<bool> ApplyToTargetProperty;
    private ITransform? _transform;
    private RelativePoint _transformOrigin = RelativePoint.Center;
    private BitmapInterpolationMode _interpolationMode;
    private bool _applyToTarget;

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

        BitmapInterpolationModeProperty =
            ConfigureProperty<BitmapInterpolationMode, TransformEffect>(nameof(BitmapInterpolationMode))
                .Accessor(o => o.BitmapInterpolationMode, (o, v) => o.BitmapInterpolationMode = v)
                .DefaultValue(BitmapInterpolationMode.Default)
                .Register();

        ApplyToTargetProperty = ConfigureProperty<bool, TransformEffect>(nameof(ApplyToTarget))
            .Accessor(o => o.ApplyToTarget, (o, v) => o.ApplyToTarget = v)
            .DefaultValue(true)
            .Register();

        AffectsRender<TransformEffect>(TransformProperty, TransformOriginProperty, BitmapInterpolationModeProperty, ApplyToTargetProperty);
        Hierarchy<TransformEffect>(TransformProperty);
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

    public bool ApplyToTarget
    {
        get => _applyToTarget;
        set => SetAndRaise(ApplyToTargetProperty, ref _applyToTarget, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        if (_transform is { IsEnabled: true, Value: Matrix mat })
        {
            if (!ApplyToTarget)
            {
                Vector origin = TransformOrigin.ToPixels(context.Bounds.Size) + context.Bounds.Position;
                Matrix offset = Matrix.CreateTranslation(origin);

                mat = (-offset) * mat * offset;
                context.Transform(mat, BitmapInterpolationMode);
            }
            else
            {
                context.CustomEffect(mat, (originalMat, c) =>
                    c.ForEach((_, target) =>
                    {
                        var surface = target.Surface?.Value!;
                        Vector origin = TransformOrigin.ToPixels(target.Bounds.Size);
                        Matrix offset1 = Matrix.CreateTranslation(origin + target.Bounds.Position);
                        Matrix offset2 = Matrix.CreateTranslation(origin);
                        var m1 = -offset1 * originalMat * offset1;
                        var m2 = -offset2 * originalMat * offset2;

                        var newTarget = c.CreateTarget(target.Bounds.TransformToAABB(m1));
                        using var canvas = c.Open(newTarget);
                        using (canvas.PushTransform(Matrix.CreateTranslation(target.Bounds.Position - newTarget.Bounds.Position)))
                        using (canvas.PushTransform(m2))
                        {
                            canvas.DrawSurface(surface, default);
                        }

                        target.Dispose();
                        return newTarget;
                    }));
            }
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
