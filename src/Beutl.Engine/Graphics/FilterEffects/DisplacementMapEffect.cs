using System.ComponentModel.DataAnnotations;
using System.Runtime.ExceptionServices;
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
            FilterEffectBrush mapBrush = context.RegisterBrush(displacementMap);
            context.CustomEffect(mapBrush,
                static (brush, effectContext) =>
                {
                    for (int i = 0; i < effectContext.Targets.Count; i++)
                    {
                        EffectTarget effectTarget = effectContext.Targets[i];
                        // Create target first so the map brush uses the buffer's post-clamp density.
                        var newTarget = effectContext.CreateTarget(effectTarget.Bounds);
                        RenderAndCommitReplacement(
                            effectContext,
                            i,
                            effectTarget,
                            newTarget,
                            (Context: effectContext, Brush: brush, Original: effectTarget, Replacement: newTarget),
                            static state =>
                            {
                                float w = state.Replacement.Scale.Value;
                                using SKShader displacementMapShader = DisplacementMapShaderFactory.CreateOrTransparent(
                                    state.Context,
                                    state.Brush,
                                    new Rect(state.Original.Bounds.Size),
                                    w);

                                using (var paint = new SKPaint())
                                using (var canvas = state.Context.Open(state.Replacement))
                                {
                                    paint.Shader = displacementMapShader;
                                    canvas.Clear();
                                    // The base CTM CreateScale(w) maps the logical DrawRect onto the full
                                    // ceil(bounds × w) device buffer; no manual prescale. w == 1 = bare logical rect.
                                    canvas.Canvas.DrawRect(
                                        new SKRect(0, 0, state.Original.Bounds.Width, state.Original.Bounds.Height),
                                        paint);
                                }
                            });
                    }
                });
        }
        else if (r.Transform is { } transform)
        {
            transform.ApplyTo(
                context.RegisterBrush(displacementMap), r.SpreadMethod, r.Channel, r.Signed, context);
        }
    }

    internal static void RenderAndCommitReplacement<TState>(
        CustomFilterEffectContext context,
        int index,
        EffectTarget original,
        EffectTarget replacement,
        TState state,
        Action<TState> draw)
    {
        try
        {
            draw(state);
        }
        catch (Exception ex)
        {
            ExceptionDispatchInfo primary = ExceptionDispatchInfo.Capture(ex);
            try
            {
                replacement.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                ex.Data["DisplacementMapReplacementCleanupFailure"] = cleanupFailure;
            }

            primary.Throw();
        }

        context.Targets[index] = replacement;
        original.Dispose();
    }
}
