using Beutl.Composition;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public interface IRenderer : IDisposable
{
    PixelSize FrameSize { get; }

    TimeSpan Time { get; }

    bool DrawFps { get; set; }

    bool IsDisposed { get; }

    bool IsGraphicsRendering { get; }

    void Render(CompositionFrame frame);

    Bitmap Snapshot();

    Drawable? HitTest(CompositionFrame frame, Point point);

    void UpdateFrame(CompositionFrame frame);

    Rect[] GetBoundaries(int zIndex);

    DrawableRenderNode? FindRenderNode(Drawable drawable);

    RenderCacheOptions CacheOptions { get; set; }
}
