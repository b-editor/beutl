using Beutl.Animation;

using SkiaSharp;

namespace Beutl.Graphics.Filters;

//public sealed class MatrixImageFilter

public sealed class DisplacementMap : ImageFilter
{
    public static readonly CoreProperty<SKColorChannel> XChannelSelectorProperty;
    public static readonly CoreProperty<SKColorChannel> YChannelSelectorProperty;
    public static readonly CoreProperty<float> ScaleProperty;
    public static readonly CoreProperty<IImageFilter?> DisplacementProperty;
    private SKColorChannel _xChannelSelector = SKColorChannel.A;
    private SKColorChannel _yChannelSelector = SKColorChannel.A;
    private float _scale;
    private IImageFilter? _displacement;

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

        DisplacementProperty = ConfigureProperty<IImageFilter?, DisplacementMap>(o => o.Displacement)
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

    public IImageFilter? Displacement
    {
        get => _displacement;
        set => SetAndRaise(DisplacementProperty, ref _displacement, value);
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        (Displacement as Animatable)?.ApplyAnimations(clock);
    }

    protected internal override SKImageFilter? ToSKImageFilter(Rect bounds)
    {
        if (Displacement?.IsEnabled == true)
        {
            var displacement = Displacement.ToSKImageFilter(bounds);
            if (displacement != null)
            {
                return SKImageFilter.CreateDisplacementMapEffect(
                    XChannelSelector,
                    YChannelSelector,
                    Scale,
                    displacement);
            }
        }

        return null;
    }
}
