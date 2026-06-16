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

    bool DrawFps { get; set; }

    bool IsDisposed { get; }

    bool IsGraphicsRendering { get; }

    void Render(CompositionFrame frame);

    Bitmap Snapshot();

    Drawable? HitTest(CompositionFrame frame, Point point);

    void UpdateFrame(CompositionFrame frame);

    Rect[] GetBoundaries(int zIndex);

    Rect? GetBoundary(Drawable drawable) => null;

    DrawableRenderNode? FindRenderNode(Drawable drawable);

    RenderCacheOptions CacheOptions { get; set; }
}
