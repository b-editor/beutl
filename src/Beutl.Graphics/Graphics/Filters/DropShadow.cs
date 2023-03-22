using System.ComponentModel.DataAnnotations;

using Beutl.Language;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.Graphics.Filters;

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
            .Register();

        SigmaProperty = ConfigureProperty<Vector, DropShadow>(nameof(Sigma))
            .Accessor(o => o.Sigma, (o, v) => o.Sigma = v)
            .DefaultValue(Vector.Zero)
            .Register();

        ColorProperty = ConfigureProperty<Color, DropShadow>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .DefaultValue(Colors.Transparent)
            .Register();

        ShadowOnlyProperty = ConfigureProperty<bool, DropShadow>(nameof(ShadowOnly))
            .Accessor(o => o.ShadowOnly, (o, v) => o.ShadowOnly = v)
            .DefaultValue(false)
            .Register();

        AffectsRender<DropShadow>(PositionProperty, SigmaProperty, ColorProperty, ShadowOnlyProperty);
    }


    [Display(Name = nameof(Strings.Position), ResourceType = typeof(Strings))]
    public Point Position
    {
        get => _position;
        set => SetAndRaise(PositionProperty, ref _position, value);
    }

    [Display(Name = nameof(Strings.Sigma), ResourceType = typeof(Strings))]
    public Vector Sigma
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
