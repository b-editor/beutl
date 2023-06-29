using SkiaSharp;

namespace Beutl.Graphics.Filters;

public interface IImageFilter
{
    bool IsEnabled { get; }

    Rect TransformBounds(Rect rect);

    SKImageFilter? ToSKImageFilter(Rect bounds);
}
