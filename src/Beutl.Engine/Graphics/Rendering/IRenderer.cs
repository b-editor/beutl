using Beutl.Composition;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public interface IRenderer : IDisposable
{
    PixelSize FrameSize { get; }

    // Default implementations for source compatibility with third-party IRenderer implementations (feature 003).
    // A renderer that does not opt into resolution-independent output behaves as output scale 1.0.
    /// <summary>
    /// The raw output scale factor s_out, in device-pixels per logical unit (e.g. 2.0 = supersample 2×, 0.5 =
    /// half-resolution preview). This is the float scale itself — NOT a quality preset. It is distinct from the
    /// app-layer <c>Beutl.Models.RenderScale</c> enum (Full/Half/Quarter/FitToPreviewer), which is a UI selector
    /// that <c>RenderScale.ToFloat</c> resolves into this value.
    /// </summary>
    float RenderScale => 1f;

    // Derive from RenderScale so a third-party renderer that overrides only RenderScale still reports the
    // correct device surface (feature 003: device == ceil(FrameSize × RenderScale)). At RenderScale == 1
    // this is FrameSize, so a non-opt-in renderer is unaffected.
    PixelSize DeviceSize => new(
        (int)Math.Ceiling(FrameSize.Width * RenderScale),
        (int)Math.Ceiling(FrameSize.Height * RenderScale));

    TimeSpan Time { get; }

    bool DrawFps { get; set; }

    bool IsDisposed { get; }

    bool IsGraphicsRendering { get; }

    void Render(CompositionFrame frame);

    Bitmap Snapshot();

    Drawable? HitTest(CompositionFrame frame, Point point);

    void UpdateFrame(CompositionFrame frame);

    Rect[] GetBoundaries(int zIndex);

    // Provide a default implementation for source compatibility with third-party IRenderer implementations. null = not computed / cache miss.
    Rect? GetBoundary(Drawable drawable) => null;

    DrawableRenderNode? FindRenderNode(Drawable drawable);

    RenderCacheOptions CacheOptions { get; set; }
}
