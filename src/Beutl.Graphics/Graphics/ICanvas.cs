using Beutl.Graphics.Filters;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.TextFormatting;

using SkiaSharp;

namespace Beutl.Graphics;

public interface ICanvas : IDisposable
{
    PixelSize Size { get; }

    bool IsDisposed { get; }

    BlendMode BlendMode { get; set; }

    Matrix Transform { get; set; }

    void Clear();

    void Clear(Color color);

    void DrawBitmap(IBitmap bmp, IBrush? fill, IPen? pen);

    void DrawEllipse(Rect rect, IBrush? fill, IPen? pen);

    void DrawRectangle(Rect rect, IBrush? fill, IPen? pen);

    void DrawGeometry(Geometry geometry, IBrush? fill, IPen? pen);
    
    void DrawText(FormattedText text, IBrush? fill, IPen? pen);

    Bitmap<Bgra8888> GetBitmap();

    void Pop(int count = -1);

    PushedState Push();

    PushedState PushClip(Rect clip, ClipOperation operation = ClipOperation.Intersect);
    
    PushedState PushClip(Geometry geometry, ClipOperation operation = ClipOperation.Intersect);

    PushedState PushOpacityMask(IBrush mask, Rect bounds, bool invert = false);

    PushedState PushImageFilter(IImageFilter filter, Rect bounds);

    PushedState PushBlendMode(BlendMode blendMode);

    PushedState PushTransform(Matrix matrix, TransformOperator transformOperator = TransformOperator.Prepend);
}

public enum TransformOperator
{
    Prepend,

    Append,

    Set
}
