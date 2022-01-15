
using SkiaSharp;

namespace BEditorNext.Graphics.Filters;

public sealed class Blur : ImageFilter
{
    public Vector Sigma { get; set; }

    public override Rect TransformBounds(Rect rect)
    {
        return rect.Inflate(new Thickness(Sigma.X, Sigma.Y));
    }

    protected internal override SKImageFilter ToSKImageFilter()
    {
        return SKImageFilter.CreateBlur(Sigma.X, Sigma.Y);
    }
}
