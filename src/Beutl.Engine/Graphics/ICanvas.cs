using Beutl.Animation;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.Media.TextFormatting;

namespace Beutl.Graphics;

public interface ICanvas : IDisposable, IPopable
{
    PixelSize Size { get; }

    bool IsDisposed { get; }

    void Clear();

    void Clear(Color color);

    void DrawImageSource(IImageSource source, Brush.Resource? fill, Pen.Resource? pen);

    void DrawVideoSource(IVideoSource source, TimeSpan frame, Brush.Resource? fill, Pen.Resource? pen);

    void DrawVideoSource(IVideoSource source, int frame, Brush.Resource? fill, Pen.Resource? pen);

    void DrawEllipse(Rect rect, Brush.Resource? fill, Pen.Resource? pen);

    void DrawRectangle(Rect rect, Brush.Resource? fill, Pen.Resource? pen);

    void DrawGeometry(Geometry.Resource geometry, Brush.Resource? fill, Pen.Resource? pen);

    void DrawText(FormattedText text, Brush.Resource? fill, Pen.Resource? pen);

    void DrawDrawable(Drawable.Resource drawable);

    void DrawNode(RenderNode node);

    void DrawBackdrop(IBackdrop backdrop);

    IBackdrop Snapshot();

    PushedState Push();

    PushedState PushLayer(Rect limit = default);

    PushedState PushClip(Rect clip, ClipOperation operation = ClipOperation.Intersect);

    PushedState PushClip(Geometry.Resource geometry, ClipOperation operation = ClipOperation.Intersect);

    PushedState PushOpacity(float opacity);

    PushedState PushOpacityMask(Brush.Resource mask, Rect bounds, bool invert = false);

    PushedState PushBlendMode(BlendMode blendMode);

    PushedState PushTransform(Matrix matrix, TransformOperator transformOperator = TransformOperator.Prepend);
}

public enum TransformOperator
{
    Prepend,

    Append,

    Set
}

public interface IBackdrop
{
    void Draw(ImmediateCanvas canvas);
}

internal sealed class TmpBackdrop(Bitmap<Bgra8888> bitmap) : IBackdrop
{
    public void Draw(ImmediateCanvas canvas)
    {
        canvas.DrawBitmap(bitmap, Brushes.Resource.White, null);
    }
}
