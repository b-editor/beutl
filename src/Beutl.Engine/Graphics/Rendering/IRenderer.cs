using Beutl.Composition;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public interface IRenderer : IDisposable
{
    PixelSize FrameSize { get; }

    // Default implementations (feature 003): a third-party renderer that does not opt into
    // resolution-independent output behaves as output scale 1.0.
    /// <summary>
    /// The raw output scale factor s_out, in device-px per logical unit (2.0 = supersample 2×, 0.5 =
    /// half-resolution preview) — the float scale itself, NOT a quality preset. Matches
    /// <c>RenderNodeContext.OutputScale</c> / <c>GraphicsContext2D.OutputScale</c>; distinct from the app-layer
    /// <c>Beutl.Models.RenderScale</c> UI selector (Full/Half/Quarter/FitToPreviewer) that <c>RenderScale.ToFloat</c>
    /// resolves into this value, and from <c>IRenderer3D.SurfaceDensity</c> (the per-surface working density).
    /// </summary>
    float OutputScale => 1f;

    // device == ceil(FrameSize × OutputScale) (feature 003); derived so a renderer overriding only OutputScale
    // still reports the correct surface. At OutputScale == 1 this equals FrameSize, so a non-opt-in renderer is unaffected.
    PixelSize DeviceSize => new(
        (int)Math.Ceiling(FrameSize.Width * OutputScale),
        (int)Math.Ceiling(FrameSize.Height * OutputScale));

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
