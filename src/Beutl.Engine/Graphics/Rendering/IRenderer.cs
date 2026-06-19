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
    /// Reads the current frame into an existing <paramref name="destination"/> bitmap so repeat-snapshot
    /// callers (e.g. onion-skin compositing) can reuse one bitmap. The default copies from
    /// <see cref="Snapshot()"/>; surface-backed renderers override it with a zero-copy readback.
    /// <paramref name="destination"/> must match the size and format of <see cref="Snapshot()"/>'s
    /// output, otherwise <see cref="ArgumentException"/> is thrown.
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

        // CopyFrom only checks bytes-per-pixel, so a same-stride but different-format destination would
        // be raw-copied with the wrong gamma/alpha. Validate against the snapshot's own format.
        if (destination.ColorType != snapshot.ColorType
            || destination.AlphaType != snapshot.AlphaType
            || !destination.ColorSpace.Equals(snapshot.ColorSpace))
        {
            throw new ArgumentException(
                $"Destination bitmap format ({destination.ColorType}/{destination.AlphaType}) must match the snapshot format ({snapshot.ColorType}/{snapshot.AlphaType}), including color space.",
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
