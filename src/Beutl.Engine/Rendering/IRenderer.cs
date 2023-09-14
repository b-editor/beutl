using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Pixel;

namespace Beutl.Rendering;

public interface IRenderer : IDisposable, IImmediateCanvasFactory
{
    RenderScene RenderScene { get; }

    PixelSize FrameSize { get; }

    IClock Clock { get; }

    bool DrawFps { get; set; }

    bool IsDisposed { get; }

    bool IsGraphicsRendering { get; }

    event EventHandler<TimeSpan> RenderInvalidated;

    Drawable? HitTest(Point point);

    Bitmap<Bgra8888>? RenderGraphics(TimeSpan timeSpan);

    void RaiseInvalidated(TimeSpan timeSpan);
}
