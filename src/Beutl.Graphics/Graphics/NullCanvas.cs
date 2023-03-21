using Beutl.Graphics.Filters;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.TextFormatting;

using SkiaSharp;

namespace Beutl.Graphics;

internal sealed class NullCanvas : ICanvas
{
    public PixelSize Size => throw new NotImplementedException();

    public bool IsDisposed => throw new NotImplementedException();

    public IBrush Foreground
    {
        get => throw new NotImplementedException();
        set => throw new NotImplementedException();
    }

    public IImageFilter? Filter
    {
        get => throw new NotImplementedException();
        set => throw new NotImplementedException();
    }

    public float StrokeWidth
    {
        get => throw new NotImplementedException();
        set => throw new NotImplementedException();
    }

    public BlendMode BlendMode
    {
        get => throw new NotImplementedException();
        set => throw new NotImplementedException();
    }
    public Matrix Transform
    {
        get => throw new NotImplementedException();
        set => throw new NotImplementedException();
    }

    public void Clear() => throw new NotImplementedException();

    public void Clear(Color color) => throw new NotImplementedException();

    public void ClipRect(Rect clip, ClipOperation operation = ClipOperation.Intersect) => throw new NotImplementedException();

    public void ClipPath(SKPath path, ClipOperation operation = ClipOperation.Intersect) => throw new NotImplementedException();

    public void Dispose() => throw new NotImplementedException();

    public void DrawBitmap(IBitmap bmp) => throw new NotImplementedException();

    public void DrawCircle(Size size) => throw new NotImplementedException();

    public void DrawRect(Size size) => throw new NotImplementedException();

    public void DrawText(FormattedText text) => throw new NotImplementedException();

    public void FillCircle(Size size) => throw new NotImplementedException();

    public void FillRect(Size size) => throw new NotImplementedException();

    public Bitmap<Bgra8888> GetBitmap() => throw new NotImplementedException();

    public void PopBlendMode(int level = -1) => throw new NotImplementedException();

    public void PopCanvas(int level = -1) => throw new NotImplementedException();

    public void PopClip(int level = -1) => throw new NotImplementedException();

    public void PopFilters(int level = -1) => throw new NotImplementedException();

    public void PopForeground(int level = -1) => throw new NotImplementedException();

    public void PopOpacityMask(int level = -1) => throw new NotImplementedException();

    public void PopStrokeWidth(int level = -1) => throw new NotImplementedException();

    public void PopTransform(int level = -1) => throw new NotImplementedException();

    public PushedState PushBlendMode(BlendMode blendMode) => throw new NotImplementedException();

    public PushedState PushCanvas() => throw new NotImplementedException();

    public PushedState PushClip(Rect clip, ClipOperation operation = ClipOperation.Intersect) => throw new NotImplementedException();

    public PushedState PushFilters(IImageFilter? filter) => throw new NotImplementedException();

    public PushedState PushForeground(IBrush brush) => throw new NotImplementedException();

    public PushedState PushOpacityMask(IBrush mask, Rect bounds, bool invert = false) => throw new NotImplementedException();

    public PushedState PushStrokeWidth(float strokeWidth) => throw new NotImplementedException();

    public PushedState PushTransform(Matrix matrix, TransformOperator transformOperator = TransformOperator.Prepend) => throw new NotImplementedException();

    public void RotateDegrees(float degrees) => throw new NotImplementedException();

    public void RotateRadians(float radians) => throw new NotImplementedException();

    public void Scale(Vector vector) => throw new NotImplementedException();

    public void Skew(Vector vector) => throw new NotImplementedException();

    public void Translate(Vector vector) => throw new NotImplementedException();
}
