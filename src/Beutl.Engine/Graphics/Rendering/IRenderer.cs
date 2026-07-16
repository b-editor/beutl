using Beutl.Composition;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public interface IRenderer : IDisposable
{
    /// <summary>The preview/delivery policy accepted by this renderer.</summary>
    RenderIntent RenderIntent => RenderIntent.Preview;

    PixelSize FrameSize { get; }

    /// <summary>
    /// Output scale factor in device-px per logical unit (e.g. 2.0 = supersample 2x). Default 1.0.
    /// </summary>
    float OutputScale => 1f;

    /// <summary>Device surface size: <c>ceil(FrameSize * OutputScale)</c>.</summary>
    PixelSize DeviceSize => new(
        (int)Math.Ceiling(FrameSize.Width * OutputScale),
        (int)Math.Ceiling(FrameSize.Height * OutputScale));

    /// <summary>
    /// Effect-pipeline counters accumulated across this renderer's render/pull calls
    /// (contracts/execution-plan.md §C8). <see langword="null"/> when the implementation does not
    /// observe the pipeline.
    /// </summary>
    PipelineDiagnostics? Diagnostics => null;

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

    /// <summary>
    /// Updates this renderer from an auxiliary composition frame before measuring the requested layer boundaries.
    /// Build <paramref name="frame"/> with
    /// <see cref="ICompositor.EvaluateGraphics(TimeSpan, RenderPullPurpose)"/> and
    /// <see cref="RenderPullPurpose.Auxiliary"/> so composition-time node evaluation and the render-tree pull use
    /// the same policy.
    /// </summary>
    Rect[] GetBoundaries(CompositionFrame frame, int zIndex)
    {
        if (frame.RenderIntent != RenderIntent || frame.PullPurpose != RenderPullPurpose.Auxiliary)
        {
            throw new ArgumentException(
                $"Boundary measurement requires a {RenderIntent} composition frame evaluated for an auxiliary pull.",
                nameof(frame));
        }

        throw new NotSupportedException(
            $"{GetType().FullName} must implement purpose-isolated boundary measurement before it can service auxiliary pulls.");
    }

    Rect? GetBoundary(Drawable drawable) => null;

    /// <summary>
    /// Updates this renderer from an auxiliary composition frame before measuring one drawable's boundary.
    /// </summary>
    Rect? GetBoundary(CompositionFrame frame, Drawable drawable)
    {
        if (frame.RenderIntent != RenderIntent || frame.PullPurpose != RenderPullPurpose.Auxiliary)
        {
            throw new ArgumentException(
                $"Boundary measurement requires a {RenderIntent} composition frame evaluated for an auxiliary pull.",
                nameof(frame));
        }

        throw new NotSupportedException(
            $"{GetType().FullName} must implement purpose-isolated boundary measurement before it can service auxiliary pulls.");
    }

    DrawableRenderNode? FindRenderNode(Drawable drawable);

    RenderCacheOptions CacheOptions { get; set; }
}
