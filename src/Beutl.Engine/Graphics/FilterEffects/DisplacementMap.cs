using Beutl.Animation;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed class DisplacementMap : FilterEffect
{
    public static readonly CoreProperty<SKColorChannel> XChannelSelectorProperty;
    public static readonly CoreProperty<SKColorChannel> YChannelSelectorProperty;
    public static readonly CoreProperty<float> ScaleProperty;
    public static readonly CoreProperty<FilterEffect?> DisplacementProperty;
    private SKColorChannel _xChannelSelector = SKColorChannel.A;
    private SKColorChannel _yChannelSelector = SKColorChannel.A;
    private float _scale = 1;
    private FilterEffect? _displacement;

    static DisplacementMap()
    {
        XChannelSelectorProperty = ConfigureProperty<SKColorChannel, DisplacementMap>(o => o.XChannelSelector)
            .DefaultValue(SKColorChannel.A)
            .Register();

        YChannelSelectorProperty = ConfigureProperty<SKColorChannel, DisplacementMap>(o => o.YChannelSelector)
            .DefaultValue(SKColorChannel.A)
            .Register();

        ScaleProperty = ConfigureProperty<float, DisplacementMap>(o => o.Scale)
            .DefaultValue(1f)
            .Register();

        DisplacementProperty = ConfigureProperty<FilterEffect?, DisplacementMap>(o => o.Displacement)
            .DefaultValue(null)
            .Register();

        AffectsRender<DisplacementMap>(XChannelSelectorProperty, YChannelSelectorProperty, ScaleProperty, DisplacementProperty);
    }

    public SKColorChannel XChannelSelector
    {
        get => _xChannelSelector;
        set => SetAndRaise(XChannelSelectorProperty, ref _xChannelSelector, value);
    }

    public SKColorChannel YChannelSelector
    {
        get => _yChannelSelector;
        set => SetAndRaise(YChannelSelectorProperty, ref _yChannelSelector, value);
    }

    public float Scale
    {
        get => _scale;
        set => SetAndRaise(ScaleProperty, ref _scale, value);
    }

    public FilterEffect? Displacement
    {
        get => _displacement;
        set => SetAndRaise(DisplacementProperty, ref _displacement, value);
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        (Displacement as Animatable)?.ApplyAnimations(clock);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        if (Displacement?.IsEnabled == true)
        {
            context.DisplacementMap(XChannelSelector, YChannelSelector, Scale, Displacement);
        }
    }

    public override Rect TransformBounds(Rect bounds)
    {
        if (Displacement?.IsEnabled == true)
        {
            bounds = bounds.Inflate(_scale / 2);
        }

        return bounds;
    }
}
