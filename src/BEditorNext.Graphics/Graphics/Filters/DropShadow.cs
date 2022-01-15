
using BEditorNext.Media;

using SkiaSharp;

namespace BEditorNext.Graphics.Filters;

public sealed class DropShadow : ImageFilter
{
    public Point Position { get; set; }

    public Vector Sigma { get; set; }

    public Color Color { get; set; }

    public bool ShadowOnly { get; set; }

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
