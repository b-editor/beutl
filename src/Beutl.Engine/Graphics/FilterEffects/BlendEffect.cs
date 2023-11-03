using Beutl.Media;

namespace Beutl.Graphics.Effects;

public sealed class BlendEffect : FilterEffect
{
    public static readonly CoreProperty<Color> ColorProperty;
    public static readonly CoreProperty<BlendMode> BlendModeProperty;
    private Color _color;
    private BlendMode _blendMode;

    static BlendEffect()
    {
        ColorProperty = ConfigureProperty<Color, BlendEffect>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .Register();

        BlendModeProperty = ConfigureProperty<BlendMode, BlendEffect>(nameof(BlendMode))
            .Accessor(o => o.BlendMode, (o, v) => o.BlendMode = v)
            .Register();

        AffectsRender<BlendEffect>(ColorProperty, BlendModeProperty);
    }

    public Color Color
    {
        get => _color;
        set => SetAndRaise(ColorProperty, ref _color, value);
    }

    public BlendMode BlendMode
    {
        get => _blendMode;
        set => SetAndRaise(BlendModeProperty, ref _blendMode, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.BlendMode(Color, BlendMode);
    }
}
