using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public class DisplacementMapEffect : FilterEffect
{
    public static readonly CoreProperty<IBrush?> DisplacementMapProperty;
    public static readonly CoreProperty<DisplacementMapTransform?> TransformProperty;
    public static readonly CoreProperty<GradientSpreadMethod> SpreadMethodProperty;
    public static readonly CoreProperty<bool> ShowDisplacementMapProperty;
    private IBrush? _displacementMap;
    private DisplacementMapTransform? _transform;
    private GradientSpreadMethod _spreadMethod = GradientSpreadMethod.Pad;
    private bool _showDisplacementMap;

    static DisplacementMapEffect()
    {
        DisplacementMapProperty = ConfigureProperty<IBrush?, DisplacementMapEffect>(nameof(DisplacementMap))
            .Accessor(o => o.DisplacementMap, (o, v) => o.DisplacementMap = v)
            .Register();

        TransformProperty = ConfigureProperty<DisplacementMapTransform?, DisplacementMapEffect>(nameof(Transform))
            .Accessor(o => o.Transform, (o, v) => o.Transform = v)
            .Register();

        SpreadMethodProperty = ConfigureProperty<GradientSpreadMethod, DisplacementMapEffect>(nameof(SpreadMethod))
            .Accessor(o => o.SpreadMethod, (o, v) => o.SpreadMethod = v)
            .DefaultValue(GradientSpreadMethod.Pad)
            .Register();

        ShowDisplacementMapProperty =
            ConfigureProperty<bool, DisplacementMapEffect>(nameof(ShowDisplacementMap))
                .Accessor(o => o.ShowDisplacementMap, (o, v) => o.ShowDisplacementMap = v)
                .Register();

        AffectsRender<DisplacementMapEffect>(
            DisplacementMapProperty,
            TransformProperty,
            SpreadMethodProperty,
            ShowDisplacementMapProperty);
    }

    public DisplacementMapEffect()
    {
        DisplacementMap = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Colors.White, 0),
                new GradientStop(Colors.Transparent, 1)
            }
        };
        Transform = new DisplacementMapTranslateTransform
        {
            X = 0,
            Y = 0
        };
    }

    [Display(Name = nameof(Strings.DisplacementMap), ResourceType = typeof(Strings))]
    public IBrush? DisplacementMap
    {
        get => _displacementMap;
        set => SetAndRaise(DisplacementMapProperty, ref _displacementMap, value);
    }

    [Display(Name = nameof(Strings.Transform), ResourceType = typeof(Strings))]
    public DisplacementMapTransform? Transform
    {
        get => _transform;
        set => SetAndRaise(TransformProperty, ref _transform, value);
    }

    [Display(Name = nameof(Strings.SpreadMethod), ResourceType = typeof(Strings))]
    public GradientSpreadMethod SpreadMethod
    {
        get => _spreadMethod;
        set => SetAndRaise(SpreadMethodProperty, ref _spreadMethod, value);
    }

    [Display(Name = nameof(Strings.ShowDisplacementMap), ResourceType = typeof(Strings))]
    public bool ShowDisplacementMap
    {
        get => _showDisplacementMap;
        set => SetAndRaise(ShowDisplacementMapProperty, ref _showDisplacementMap, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        if (DisplacementMap is null) return;
        var displacementMap = (DisplacementMap as IMutableBrush)?.ToImmutable() ?? DisplacementMap;

        if (ShowDisplacementMap)
        {
            context.CustomEffect(displacementMap,
                (d, c) =>
                {
                    for (int i = 0; i < c.Targets.Count; i++)
                    {
                        EffectTarget effectTarget = c.Targets[i];
                        using var displacementMapShader =
                            new BrushConstructor(new Rect(effectTarget.Bounds.Size), d, BlendMode.SrcOver)
                                .CreateShader();

                        using (var paint = new SKPaint())
                        {
                            var newTarget = c.CreateTarget(effectTarget.Bounds);
                            var canvas = newTarget.RenderTarget!.Value.Canvas;
                            paint.Shader = displacementMapShader;
                            canvas.DrawRect(new SKRect(0, 0, effectTarget.Bounds.Width, effectTarget.Bounds.Height),
                                paint);

                            c.Targets[i] = newTarget;
                        }

                        effectTarget.Dispose();
                    }
                });
        }
        else if (Transform is not null)
        {
            Transform.ApplyTo(displacementMap, _spreadMethod, context);
        }
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        (DisplacementMap as IAnimatable)?.ApplyAnimations(clock);
        Transform?.ApplyAnimations(clock);
    }
}
