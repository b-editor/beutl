using Beutl.Animation;
using Beutl.Media;
using Beutl.Media.Pixel;

namespace Beutl.Graphics.Rendering;

public interface IRenderer : IDisposable
{
    RenderScene RenderScene { get; }

    PixelSize FrameSize { get; }

    IClock Clock { get; }

    bool DrawFps { get; set; }

    bool IsDisposed { get; }

    bool IsGraphicsRendering { get; }

    Drawable? HitTest(Point point);

    [Obsolete("Use Render(TimeSpan) and Snapshot() instead of RenderGraphics.")]
    Bitmap<Bgra8888>? RenderGraphics(TimeSpan timeSpan);

    bool Render(TimeSpan timeSpan);

    Bitmap<Bgra8888> Snapshot();
}
