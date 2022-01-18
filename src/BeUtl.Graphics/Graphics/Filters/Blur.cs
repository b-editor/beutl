
using SkiaSharp;

namespace BeUtl.Graphics.Filters;

public sealed class Blur : ImageFilter
{
    private Vector _sigma;

    public Vector Sigma
    {
        get => _sigma;
        set => SetProperty(ref _sigma, value);
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
