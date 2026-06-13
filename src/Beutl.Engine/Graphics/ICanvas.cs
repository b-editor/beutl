using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Media.TextFormatting;

namespace Beutl.Graphics;

public interface ICanvas : IDisposable, IPopable
{
    /// <summary>The declared LOGICAL viewport (feature 003); the base CTM maps it onto <see cref="DeviceSize"/>.</summary>
    Size LogicalSize { get; }

    /// <summary>The physical backing-surface size in device pixels (feature 003).</summary>
    PixelSize DeviceSize { get; }

    /// <summary>Device pixels per unit of the CURRENT coordinate space (feature 003); 1 inside <see cref="PushDeviceSpace"/>.</summary>
    float Density { get; }

    /// <summary>The immutable device-pixels-per-logical-unit the surface is rasterized at (feature 003).</summary>
    float SurfaceDensity { get; }

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

    /// <summary>
    /// feature 003: enter ABSOLUTE device space (CTM identity, <see cref="Density"/> = 1) for the lifetime
    /// of the returned state — for drawing device-px content (a contour traced from the device buffer, a
    /// point-blit of another device buffer, a full-buffer shader rect) onto a density-aware canvas.
    /// </summary>
    PushedState PushDeviceSpace();
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
        // feature 003 (CSM-3/CSM3-1): the capture is the device-sized backing surface
        // (ceil(frame × captureScale) px). Un-scale by the SurfaceDensity it was CAPTURED at — NOT the replay
        // canvas's density — because when the backdrop is replayed inside a buffer-flushing FilterEffect, Draw
        // runs on a nested canvas whose SurfaceDensity is that buffer's working density w (≠ the capture
        // density); keying off that would mis-size the capture under the flush's baked CreateScale(w) base CTM.
        // Mapping it into its logical footprint lets the active CTM map it back. captureScale == 1 = bare blit.
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
