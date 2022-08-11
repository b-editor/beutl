using SkiaSharp;

namespace BeUtl.Graphics.Filters;

public interface IImageFilter
{
    bool IsEnabled { get; }

    Rect TransformBounds(Rect rect);

    SKImageFilter ToSKImageFilter();
}
