using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Media.TextFormatting;

namespace Beutl.Graphics;

public interface ICanvas : IDisposable, IPopable
{
    PixelSize Size { get; }

    bool IsDisposed { get; }

    void Clear();

    void Clear(Color color);

    void DrawImageSource(ImageSource.Resource source, Brush.Resource? fill, Pen.Resource? pen);

    void DrawVideoSource(VideoSource.Resource source, TimeSpan frame, Brush.Resource? fill, Pen.Resource? pen);

    void DrawVideoSource(VideoSource.Resource source, int frame, Brush.Resource? fill, Pen.Resource? pen);

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

internal sealed class TmpBackdrop(Bitmap bitmap, float captureScale) : IBackdrop
{
    public void Draw(ImmediateCanvas canvas)
    {
        // feature 003 (CSM-3): the capture is the device-sized backing surface (ceil(frame × captureScale) px).
        // Un-scale by the scale it was CAPTURED at — NOT the replay canvas's OutputScale — because when the
        // backdrop is replayed inside a buffer-flushing FilterEffect, Draw runs on a nested canvas whose
        // OutputScale is the default 1 (only the root canvas carries s_out); keying off that would blit the
        // device capture 1:1 under the flush's CreateScale(w) CTM and render it ~s_out× too large. Mapping it
        // into its logical footprint lets the active CTM map it back. captureScale == 1 keeps the bare blit.
        if (captureScale == 1f)
        {
            canvas.DrawBitmap(bitmap, Brushes.Resource.White, null);
        }
        else
        {
            var dest = new Rect(0, 0, bitmap.Width / captureScale, bitmap.Height / captureScale);
            canvas.DrawBitmapScaled(bitmap, dest, Brushes.Resource.White);
        }
    }
}
