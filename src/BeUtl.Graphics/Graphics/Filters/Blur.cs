using SkiaSharp;

namespace BeUtl.Graphics.Filters;

public sealed class Blur : ImageFilter
{
    public static readonly CoreProperty<Vector> SigmaProperty;
    private Vector _sigma;

    static Blur()
    {
        SigmaProperty = ConfigureProperty<Vector, Blur>(nameof(Sigma))
            .Accessor(o => o.Sigma, (o, v) => o.Sigma = v)
            .DefaultValue(Vector.Zero)
            .Register();

        AffectRender<Blur>(SigmaProperty);
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
