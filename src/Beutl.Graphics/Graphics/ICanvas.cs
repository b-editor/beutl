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

    IBrush FillBrush { get; set; }

    IPen? Pen { get; set; }

    IImageFilter? Filter { get; }

    BlendMode BlendMode { get; set; }

    Matrix Transform { get; set; }

    void Clear();

    void Clear(Color color);

    void ClipRect(Rect clip, ClipOperation operation = ClipOperation.Intersect);

    void ClipPath(Geometry geometry, ClipOperation operation = ClipOperation.Intersect);

    void DrawBitmap(IBitmap bmp);

    void DrawCircle(Rect rect);

    void DrawRect(Rect rect);

    void DrawGeometry(Geometry geometry);
    
    void DrawText(FormattedText text);

    Bitmap<Bgra8888> GetBitmap();

    PushedState PushClip(Rect clip, ClipOperation operation = ClipOperation.Intersect);

    void PopClip(int level = -1);

    PushedState PushCanvas();

    void PopCanvas(int level = -1);

    PushedState PushOpacityMask(IBrush mask, Rect bounds, bool invert = false);

    void PopOpacityMask(int level = -1);

    PushedState PushFillBrush(IBrush brush);

    void PopFillBrush(int level = -1);
    
    PushedState PushPen(IPen? pen);

    void PopPen(int level = -1);

    PushedState PushImageFilter(IImageFilter filter);

    void PopImageFilter(int level = -1);

    PushedState PushBlendMode(BlendMode blendMode);

    void PopBlendMode(int level = -1);

    PushedState PushTransform(Matrix matrix, TransformOperator transformOperator = TransformOperator.Prepend);

    void PopTransform(int level = -1);
}

public enum TransformOperator
{
    Prepend,

    Append,

    Assign
}
