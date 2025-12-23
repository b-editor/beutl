using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public partial class DisplacementMapEffect : FilterEffect
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

        Transform.CurrentValue = new DisplacementMapTranslateTransform();
    }

    [Display(Name = nameof(Strings.DisplacementMap), ResourceType = typeof(Strings))]
    public IProperty<Brush?> DisplacementMap { get; } = Property.Create<Brush?>();

    [Display(Name = nameof(Strings.Transform), ResourceType = typeof(Strings))]
    public IProperty<DisplacementMapTransform?> Transform { get; } = Property.Create<DisplacementMapTransform?>();

    [Display(Name = nameof(Strings.SpreadMethod), ResourceType = typeof(Strings))]
    public IProperty<GradientSpreadMethod> SpreadMethod { get; } = Property.CreateAnimatable(GradientSpreadMethod.Pad);

    [Display(Name = nameof(Strings.ShowDisplacementMap), ResourceType = typeof(Strings))]
    public IProperty<bool> ShowDisplacementMap { get; } = Property.CreateAnimatable(false);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        Brush.Resource? displacementMap = r.DisplacementMap;
        if (displacementMap is null) return;

        if (r.ShowDisplacementMap)
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

                        var newTarget = effectContext.CreateTarget(effectTarget.Bounds);
                        using (var paint = new SKPaint())
                        using (var canvas = effectContext.Open(newTarget))
                        {
                            paint.Shader = displacementMapShader;
                            canvas.Clear();
                            canvas.Canvas.DrawRect(
                                new SKRect(0, 0, effectTarget.Bounds.Width, effectTarget.Bounds.Height),
                                paint);

                            effectContext.Targets[i] = newTarget;
                        }

                        effectTarget.Dispose();
                    }
                });
        }
        else if (r.Transform is { } transform)
        {
            transform.GetOriginal().ApplyTo(displacementMap, transform, r.SpreadMethod, context);
        }
    }
}
