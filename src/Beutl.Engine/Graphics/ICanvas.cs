using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Media.TextFormatting;

namespace Beutl.Graphics;

public interface ICanvas : IDisposable, IPopable
{
    /// <summary>The declared logical viewport; the base CTM maps it onto <see cref="DeviceSize"/>.</summary>
    Size LogicalSize => new(DeviceSize.Width, DeviceSize.Height);

    /// <summary>The physical backing-surface size in device pixels.</summary>
    PixelSize DeviceSize { get; }

    /// <summary>Device pixels per unit of the current coordinate space; 1 inside <see cref="PushDeviceSpace"/>.</summary>
    float Density => 1f;

    /// <summary>The immutable device-pixels-per-logical-unit the surface is rasterized at.</summary>
    float SurfaceDensity => 1f;

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
    /// Enter absolute device space (CTM identity, <see cref="Density"/> = 1) for device-px drawing.
    /// </summary>
    PushedState PushDeviceSpace() => Push();
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
        // Un-scale by the capture's density, not the replay canvas's density.
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
