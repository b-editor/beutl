using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public class DisplacementMapEffect : FilterEffect
{
    public DisplacementMapEffect()
    {
        ScanProperties<DisplacementMapEffect>();

        DisplacementMap.CurrentValue = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Colors.White, 0),
                new GradientStop(Colors.Transparent, 1)
            }
        };

        Transform.CurrentValue = new DisplacementMapTranslateTransform
        {
            X = 0,
            Y = 0
        };
    }

    [Display(Name = nameof(Strings.DisplacementMap), ResourceType = typeof(Strings))]
    public IProperty<IBrush?> DisplacementMap { get; } = Property.Create<IBrush?>();

    [Display(Name = nameof(Strings.Transform), ResourceType = typeof(Strings))]
    public IProperty<DisplacementMapTransform?> Transform { get; } = Property.Create<DisplacementMapTransform?>();

    [Display(Name = nameof(Strings.SpreadMethod), ResourceType = typeof(Strings))]
    public IProperty<GradientSpreadMethod> SpreadMethod { get; } = Property.CreateAnimatable(GradientSpreadMethod.Pad);

    [Display(Name = nameof(Strings.ShowDisplacementMap), ResourceType = typeof(Strings))]
    public IProperty<bool> ShowDisplacementMap { get; } = Property.CreateAnimatable(false);

    public override void ApplyTo(FilterEffectContext context)
    {
        IBrush? displacementMap = DisplacementMap.CurrentValue;
        if (displacementMap is null) return;

        displacementMap = (displacementMap as IMutableBrush)?.ToImmutable() ?? displacementMap;

        if (ShowDisplacementMap.CurrentValue)
        {
            context.CustomEffect(displacementMap,
                static (brush, effectContext) =>
                {
                    for (int i = 0; i < effectContext.Targets.Count; i++)
                    {
                        EffectTarget effectTarget = effectContext.Targets[i];
                        using var displacementMapShader =
                            new BrushConstructor(new Rect(effectTarget.Bounds.Size), brush, BlendMode.SrcOver)
                                .CreateShader();

                        using (var paint = new SKPaint())
                        {
                            var newTarget = effectContext.CreateTarget(effectTarget.Bounds);
                            var canvas = newTarget.RenderTarget!.Value.Canvas;
                            paint.Shader = displacementMapShader;
                            canvas.DrawRect(new SKRect(0, 0, effectTarget.Bounds.Width, effectTarget.Bounds.Height),
                                paint);

                            effectContext.Targets[i] = newTarget;
                        }

                        effectTarget.Dispose();
                    }
                });
        }
        else if (Transform.CurrentValue is { } transform)
        {
            transform.ApplyTo(displacementMap, SpreadMethod.CurrentValue, context);
        }
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        (DisplacementMap.CurrentValue as IAnimatable)?.ApplyAnimations(clock);
        Transform.CurrentValue?.ApplyAnimations(clock);
    }
}
