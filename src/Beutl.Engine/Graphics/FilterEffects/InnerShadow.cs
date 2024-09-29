using System.ComponentModel.DataAnnotations;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

public class InnerShadow : FilterEffect
{
    public static readonly CoreProperty<Point> PositionProperty;
    public static readonly CoreProperty<Size> SigmaProperty;
    public static readonly CoreProperty<Color> ColorProperty;
    public static readonly CoreProperty<bool> ShadowOnlyProperty;
    private Point _position;
    private Size _sigma;
    private Color _color;
    private bool _shadowOnly;

    static InnerShadow()
    {
        PositionProperty = ConfigureProperty<Point, InnerShadow>(nameof(Position))
            .Accessor(o => o.Position, (o, v) => o.Position = v)
            .DefaultValue(new Point())
            .Register();

        SigmaProperty = ConfigureProperty<Size, InnerShadow>(nameof(Sigma))
            .Accessor(o => o.Sigma, (o, v) => o.Sigma = v)
            .DefaultValue(Size.Empty)
            .Register();

        ColorProperty = ConfigureProperty<Color, InnerShadow>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .DefaultValue(Colors.Transparent)
            .Register();

        ShadowOnlyProperty = ConfigureProperty<bool, InnerShadow>(nameof(ShadowOnly))
            .Accessor(o => o.ShadowOnly, (o, v) => o.ShadowOnly = v)
            .DefaultValue(false)
            .Register();

        AffectsRender<InnerShadow>(PositionProperty, SigmaProperty, ColorProperty, ShadowOnlyProperty);
    }

    [Display(Name = nameof(Strings.Position), ResourceType = typeof(Strings))]
    public Point Position
    {
        get => _position;
        set => SetAndRaise(PositionProperty, ref _position, value);
    }

    [Display(Name = nameof(Strings.Sigma), ResourceType = typeof(Strings))]
    [Range(typeof(Size), "0,0", "max,max")]
    public Size Sigma
    {
        get => _sigma;
        set => SetAndRaise(SigmaProperty, ref _sigma, value);
    }

    [Display(Name = nameof(Strings.Color), ResourceType = typeof(Strings))]
    public Color Color
    {
        get => _color;
        set => SetAndRaise(ColorProperty, ref _color, value);
    }

    [Display(Name = nameof(Strings.ShadowOnly), ResourceType = typeof(Strings))]
    public bool ShadowOnly
    {
        get => _shadowOnly;
        set => SetAndRaise(ShadowOnlyProperty, ref _shadowOnly, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        if (ShadowOnly)
            context.InnerShadowOnly(Position, Sigma, Color);
        else
            context.InnerShadow(Position, Sigma, Color);
    }
}
