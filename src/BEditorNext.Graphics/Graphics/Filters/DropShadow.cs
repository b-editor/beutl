
using BEditorNext.Media;

using SkiaSharp;

namespace BEditorNext.Graphics.Filters;

public sealed class DropShadow : ImageFilter
{
    private Point _position;
    private Vector _sigma;
    private Color _color;
    private bool _shadowOnly;

    public Point Position
    {
        get => _position;
        set => SetProperty(ref _position, value);
    }

    public Vector Sigma
    {
        get => _sigma;
        set => SetProperty(ref _sigma, value);
    }

    public Color Color
    {
        get => _color;
        set => SetProperty(ref _color, value);
    }

    public bool ShadowOnly
    {
        get => _shadowOnly;
        set => SetProperty(ref _shadowOnly, value);
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
