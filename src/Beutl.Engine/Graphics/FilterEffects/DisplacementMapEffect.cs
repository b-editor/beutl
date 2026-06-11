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
                        // feature 003: pass the working density so a tile/image/drawable map rasterizes at w
                        // (the shader is consumed under the Scale(w) CTM below, which its baked Scale(1/w)
                        // compensates); analytic brushes ignore it. w == 1 is a no-op (byte-identical).
                        float w = effectContext.WorkingScale;
                        using var displacementMapShader =
                            new BrushConstructor(new Rect(effectTarget.Bounds.Size), brush, BlendMode.SrcOver, w)
                                .CreateShader();

                        var newTarget = effectContext.CreateTarget(effectTarget.Bounds);
                        using (var paint = new SKPaint())
                        using (var canvas = effectContext.Open(newTarget))
                        {
                            paint.Shader = displacementMapShader;
                            canvas.Clear();
                            // feature 003: fill the full ceil(bounds × w) device buffer. At w != 1 prescale so
                            // the logical DrawRect + brush shader render at working density; w == 1 keeps the
                            // bare logical rect (byte-identical).
                            using (w == 1f ? default : canvas.PushTransform(Matrix.CreateScale(w, w)))
                            {
                                canvas.Canvas.DrawRect(
                                    new SKRect(0, 0, effectTarget.Bounds.Width, effectTarget.Bounds.Height),
                                    paint);
                            }

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
