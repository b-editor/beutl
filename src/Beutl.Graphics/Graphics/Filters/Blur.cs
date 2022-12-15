using Beutl.Language;

using SkiaSharp;

namespace Beutl.Graphics.Filters;

public sealed class Blur : ImageFilter
{
    public static readonly CoreProperty<Vector> SigmaProperty;
    private Vector _sigma;

    static Blur()
    {
        SigmaProperty = ConfigureProperty<Vector, Blur>(nameof(Sigma))
            .Accessor(o => o.Sigma, (o, v) => o.Sigma = v)
            .Display(Strings.Sigma)
            .DefaultValue(Vector.Zero)
            .Minimum(Vector.Zero)
            .PropertyFlags(PropertyFlags.All)
            .SerializeName("sigma")
            .Register();

        AffectsRender<Blur>(SigmaProperty);
    }

    public Vector Sigma
    {
        get => _sigma;
        set => SetAndRaise(SigmaProperty, ref _sigma, value);
    }

    public override Rect TransformBounds(Rect rect)
    {
        return rect.Inflate(new Thickness(Sigma.X, Sigma.Y));
    }

    protected internal override SKImageFilter ToSKImageFilter()
    {
        return SKImageFilter.CreateBlur(Sigma.X, Sigma.Y);
    }
}
