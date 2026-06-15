using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.DisplacementMapEffect), ResourceType = typeof(GraphicsStrings))]
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

    [Display(Name = nameof(GraphicsStrings.DisplacementMapEffect_DisplacementMap), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Brush?> DisplacementMap { get; } = Property.Create<Brush?>();

    [Display(Name = nameof(GraphicsStrings.Transform), ResourceType = typeof(GraphicsStrings))]
    public IProperty<DisplacementMapTransform?> Transform { get; } = Property.Create<DisplacementMapTransform?>();

    [Display(Name = nameof(GraphicsStrings.DisplacementMapEffect_SpreadMethod), ResourceType = typeof(GraphicsStrings))]
    public IProperty<GradientSpreadMethod> SpreadMethod { get; } = Property.CreateAnimatable(GradientSpreadMethod.Pad);

    [Display(Name = nameof(GraphicsStrings.DisplacementMapEffect_Channel), ResourceType = typeof(GraphicsStrings))]
    public IProperty<DisplacementMapChannel> Channel { get; } = Property.Create(DisplacementMapChannel.Alpha);

    [Display(Name = nameof(GraphicsStrings.DisplacementMapEffect_Signed), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> Signed { get; } = Property.Create(false);

    [Display(Name = nameof(GraphicsStrings.DisplacementMapEffect_ShowDisplacementMap), ResourceType = typeof(GraphicsStrings))]
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
                        // feature 003: create the target first so the map brush uses the buffer's real
                        // post-clamp density (newTarget.Scale.Value), not nominal WorkingScale which would
                        // mis-densify a clamped buffer (FR-037(b)). Its baked Scale(1/w) then matches Open's
                        // base CTM CreateScale(w). Analytic brushes ignore w; w == 1 is byte-identical.
                        var newTarget = effectContext.CreateTarget(effectTarget.Bounds);
                        float w = newTarget.Scale.Value;
                        using var displacementMapShader =
                            new BrushConstructor(new Rect(effectTarget.Bounds.Size), brush, BlendMode.SrcOver, w,
                                    effectContext.MaxWorkingScale)
                                .CreateShader();

                        using (var paint = new SKPaint())
                        using (var canvas = effectContext.Open(newTarget))
                        {
                            paint.Shader = displacementMapShader;
                            canvas.Clear();
                            // The base CTM CreateScale(w) maps the logical DrawRect onto the full
                            // ceil(bounds × w) device buffer; no manual prescale. w == 1 = bare logical rect.
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
            transform.GetOriginal().ApplyTo(displacementMap, transform, r.SpreadMethod, r.Channel, r.Signed, context);
        }
    }
}
