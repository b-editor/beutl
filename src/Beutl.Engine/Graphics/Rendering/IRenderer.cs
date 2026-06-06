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

    PixelSize DeviceSize => FrameSize;

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
