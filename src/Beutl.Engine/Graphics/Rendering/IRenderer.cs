using Beutl.Composition;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public interface IRenderer : IDisposable
{
    PixelSize FrameSize { get; }

    /// <summary>
    /// Output scale factor in device-px per logical unit (e.g. 2.0 = supersample 2x). Default 1.0.
    /// </summary>
    float OutputScale => 1f;

    /// <summary>Device surface size: <c>ceil(FrameSize * OutputScale)</c>.</summary>
    PixelSize DeviceSize => new(
        (int)Math.Ceiling(FrameSize.Width * OutputScale),
        (int)Math.Ceiling(FrameSize.Height * OutputScale));

    TimeSpan Time { get; }

    bool IsDisposed { get; }

    bool IsGraphicsRendering { get; }

    void Render(CompositionFrame frame);

    Bitmap Snapshot();

    /// <summary>
    /// Reads the current frame into an existing <paramref name="destination"/> bitmap, reusing it
    /// instead of allocating a fresh snapshot — useful for callers that snapshot repeatedly
    /// (e.g. onion-skin compositing while scrubbing). The default implementation falls back to
    /// <see cref="Snapshot()"/> and copies; surface-backed renderers override it with a zero-copy
    /// readback. <paramref name="destination"/> must match the snapshot size and format.
    /// </summary>
    void SnapshotInto(Bitmap destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        using Bitmap snapshot = Snapshot();
        if (destination.Width != snapshot.Width || destination.Height != snapshot.Height)
        {
            throw new ArgumentException(
                $"Destination bitmap size ({destination.Width}x{destination.Height}) must match the snapshot size ({snapshot.Width}x{snapshot.Height}).",
                nameof(destination));
        }

        destination.CopyFrom(snapshot, new PixelRect(0, 0, snapshot.Width, snapshot.Height));
    }

    Drawable? HitTest(CompositionFrame frame, Point point);

    void UpdateFrame(CompositionFrame frame);

    Rect[] GetBoundaries(int zIndex);

    Rect? GetBoundary(Drawable drawable) => null;

    DrawableRenderNode? FindRenderNode(Drawable drawable);

    RenderCacheOptions CacheOptions { get; set; }
}
