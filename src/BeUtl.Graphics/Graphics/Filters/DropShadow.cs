
using BeUtl.Media;

using SkiaSharp;

namespace BeUtl.Graphics.Filters;

public sealed class DropShadow : ImageFilter
{
    public Point Position { get; set; }

    public Vector Sigma { get; set; }

    public Color Color { get; set; }

    public bool ShadowOnly { get; set; }

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
