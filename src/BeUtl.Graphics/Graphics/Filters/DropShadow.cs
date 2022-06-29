using BeUtl.Media;

using SkiaSharp;

namespace BeUtl.Graphics.Filters;

public sealed class DropShadow : ImageFilter
{
    public static readonly CoreProperty<Point> PositionProperty;
    public static readonly CoreProperty<Vector> SigmaProperty;
    public static readonly CoreProperty<Color> ColorProperty;
    public static readonly CoreProperty<bool> ShadowOnlyProperty;
    private Point _position;
    private Vector _sigma;
    private Color _color;
    private bool _shadowOnly;

    static DropShadow()
    {
        PositionProperty = ConfigureProperty<Point, DropShadow>(nameof(Position))
            .Accessor(o => o.Position, (o, v) => o.Position = v)
            .DefaultValue(new Point())
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .SerializeName("position")
            .Register();

        SigmaProperty = ConfigureProperty<Vector, DropShadow>(nameof(Sigma))
            .Accessor(o => o.Sigma, (o, v) => o.Sigma = v)
            .DefaultValue(Vector.Zero)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .SerializeName("sigma")
            .Register();

        ColorProperty = ConfigureProperty<Color, DropShadow>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .DefaultValue(Colors.Transparent)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .SerializeName("color")
            .Register();

        ShadowOnlyProperty = ConfigureProperty<bool, DropShadow>(nameof(ShadowOnly))
            .Accessor(o => o.ShadowOnly, (o, v) => o.ShadowOnly = v)
            .DefaultValue(false)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .SerializeName("shadow-only")
            .Register();

        AffectsRender<DropShadow>(PositionProperty, SigmaProperty, ColorProperty, ShadowOnlyProperty);
    }

    public Point Position
    {
        get => _position;
        set => SetAndRaise(PositionProperty, ref _position, value);
    }

    public Vector Sigma
    {
        get => _sigma;
        set => SetAndRaise(SigmaProperty, ref _sigma, value);
    }

    public Color Color
    {
        get => _color;
        set => SetAndRaise(ColorProperty, ref _color, value);
    }

    public bool ShadowOnly
    {
        get => _shadowOnly;
        set => SetAndRaise(ShadowOnlyProperty, ref _shadowOnly, value);
    }

    public override Rect TransformBounds(Rect rect)
    {
        Rect shadow = rect.Translate(Position).Inflate(new Thickness(Sigma.X, Sigma.Y));

        return ShadowOnly ? shadow : rect.Union(shadow);
    }

    protected internal override SKImageFilter ToSKImageFilter()
    {
        if (ShadowOnly)
        {
            return SKImageFilter.CreateDropShadowOnly(Position.X, Position.Y, Sigma.X, Sigma.Y, Color.ToSKColor());
        }
        else
        {
            return SKImageFilter.CreateDropShadow(Position.X, Position.Y, Sigma.X, Sigma.Y, Color.ToSKColor());
        }
    }
}
