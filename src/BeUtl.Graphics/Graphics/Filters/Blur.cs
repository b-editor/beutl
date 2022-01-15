
using SkiaSharp;

namespace BeUtl.Graphics.Filters;

public sealed class Blur : ImageFilter
{
    public Vector Sigma { get; set; }

    protected internal override SKImageFilter ToSKImageFilter()
    {
        return SKImageFilter.CreateBlur(Sigma.X, Sigma.Y);
    }
}
