using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.Media.TextFormatting;

namespace Beutl.Graphics;

public interface ICanvas : IDisposable
{
    PixelSize Size { get; }

    bool IsDisposed { get; }

    BlendMode BlendMode { get; }

    Matrix Transform { get; }

    void Clear();

    void Clear(Color color);

    void DrawImageSource(IImageSource source, IBrush? fill, IPen? pen);

    void DrawVideoSource(IVideoSource source, TimeSpan frame, IBrush? fill, IPen? pen);

    void DrawVideoSource(IVideoSource source, int frame, IBrush? fill, IPen? pen);

    void DrawEllipse(Rect rect, IBrush? fill, IPen? pen);

    void DrawRectangle(Rect rect, IBrush? fill, IPen? pen);

    void DrawGeometry(Geometry geometry, IBrush? fill, IPen? pen);

    void DrawText(FormattedText text, IBrush? fill, IPen? pen);

    void DrawDrawable(Drawable drawable);

    void DrawNode(IGraphicNode node);

    void DrawBackdrop(IBackdrop backdrop);

    IBackdrop Snapshot();

    Bitmap<Bgra8888> GetBitmap();

    void Pop(int count = -1);

    PushedState Push();

    PushedState PushLayer(Rect limit = default);

    PushedState PushClip(Rect clip, ClipOperation operation = ClipOperation.Intersect);

    PushedState PushClip(Geometry geometry, ClipOperation operation = ClipOperation.Intersect);

    PushedState PushOpacityMask(IBrush mask, Rect bounds, bool invert = false);

    PushedState PushFilterEffect(FilterEffect effect);

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
        canvas.DrawBitmap(bitmap, Brushes.White, null);
    }
}
