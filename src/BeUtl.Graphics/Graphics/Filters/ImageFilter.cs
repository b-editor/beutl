using BeUtl.Media;
using BeUtl.Styling;

using SkiaSharp;

namespace BeUtl.Graphics.Filters;

public interface IImageFilter : IStyleable, IAffectsRender
{
    Rect TransformBounds(Rect rect);

    SKImageFilter ToSKImageFilter();
}

public abstract class ImageFilter : Styleable, IImageFilter
{
    public event EventHandler? Invalidated;

    public virtual Rect TransformBounds(Rect rect)
    {
        return rect;
    }

    protected internal abstract SKImageFilter ToSKImageFilter();

    protected static void AffectRender<T>(params CoreProperty[] properties)
        where T : ImageFilter
    {
        foreach (CoreProperty? item in properties)
        {
            item.Changed.Subscribe(e =>
            {
                if (e.Sender is T s)
                {
                    s.RaiseInvalidated();
                }
            });
        }
    }

    protected void RaiseInvalidated()
    {
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    SKImageFilter IImageFilter.ToSKImageFilter()
    {
        return ToSKImageFilter();
    }
}
