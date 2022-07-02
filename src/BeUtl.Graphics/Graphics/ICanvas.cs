using BeUtl.Graphics.Filters;
using BeUtl.Media;
using BeUtl.Media.Pixel;
using BeUtl.Media.TextFormatting;

using SkiaSharp;

namespace BeUtl.Graphics;

public interface ICanvas : IDisposable
{
    PixelSize Size { get; }

    bool IsDisposed { get; }

    IBrush Foreground { get; set; }

    IImageFilter? Filter { get; set; }

    float StrokeWidth { get; set; }

    BlendMode BlendMode { get; set; }

    Matrix Transform { get; set; }

    void Clear();

    void Clear(Color color);

    void ClipRect(Rect clip, ClipOperation operation = ClipOperation.Intersect);

    void ClipPath(SKPath path, ClipOperation operation = ClipOperation.Intersect);

    void DrawBitmap(IBitmap bmp);

    void DrawCircle(Size size);

    void DrawRect(Size size);

    void FillCircle(Size size);

    void FillRect(Size size);

    void DrawText(TextElement text, Size size);

    Bitmap<Bgra8888> GetBitmap();

    PushedState PushClip(Rect clip, ClipOperation operation = ClipOperation.Intersect);

    void PopClip(int level = -1);

    PushedState PushCanvas();

    void PopCanvas(int level = -1);

    PushedState PushOpacityMask(IBrush mask, Rect bounds, bool invert = false);

    void PopOpacityMask(int level = -1);

    PushedState PushForeground(IBrush brush);

    void PopForeground(int level = -1);

    PushedState PushStrokeWidth(float strokeWidth);

    void PopStrokeWidth(int level = -1);

    PushedState PushFilters(IImageFilter? filter);

    void PopFilters(int level = -1);

    PushedState PushBlendMode(BlendMode blendMode);

    void PopBlendMode(int level = -1);

    PushedState PushTransform(Matrix matrix, TransformOperator transformOperator = TransformOperator.Prepend);

    void PopTransform(int level = -1);

    void RotateDegrees(float degrees);

    void RotateRadians(float radians);

    void Scale(Vector vector);

    void Skew(Vector vector);

    void Translate(Vector vector);
}

public enum TransformOperator
{
    Prepend,

    Append,

    Assign
}
