using Beutl.Composition;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public interface IRenderer : IDisposable
{
    PixelSize FrameSize { get; }

    /// <summary>
    /// Output scale factor in device-px per logical unit (e.g. 2.0 = supersample 2x).
    /// </summary>
    float OutputScale { get; }

    /// <summary>Device surface size: <c>ceil(FrameSize * OutputScale)</c>.</summary>
    PixelSize DeviceSize { get; }

    TimeSpan Time { get; }

    bool IsDisposed { get; }

    bool IsGraphicsRendering { get; }

    void Render(CompositionFrame frame);

    Bitmap Snapshot();

    /// <summary>
    /// Reads the current frame into an existing <paramref name="destination"/> bitmap so repeat-snapshot
    /// callers (e.g. onion-skin compositing) can reuse one bitmap.
    /// <paramref name="destination"/> must match the size and format of <see cref="Snapshot()"/>'s
    /// output, otherwise <see cref="ArgumentException"/> is thrown.
    /// </summary>
    void SnapshotInto(Bitmap destination);

    Drawable? HitTest(CompositionFrame frame, Point point);

    void UpdateFrame(CompositionFrame frame);

    /// <summary>Gets the current-frame bounds for drawables at the specified z-index.</summary>
    /// <remarks>
    /// Call this method on the render thread. Implementations may resolve and cache deferred render-graph metadata
    /// when the bounds are first queried after <see cref="Render"/> or <see cref="UpdateFrame"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The caller does not have render-thread access.</exception>
    Rect[] GetBoundaries(int zIndex);

    /// <summary>Gets the current-frame bounds for one drawable, or <see langword="null"/> when it is not present.</summary>
    /// <remarks>Implementations that evaluate deferred metadata require render-thread access.</remarks>
    /// <exception cref="InvalidOperationException">The caller does not have render-thread access.</exception>
    Rect? GetBoundary(Drawable drawable);

    DrawableRenderNode? FindRenderNode(Drawable drawable);

    RenderCacheOptions CacheOptions { get; set; }
}
