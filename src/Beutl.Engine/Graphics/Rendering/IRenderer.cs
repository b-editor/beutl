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

    bool Render(TimeSpan timeSpan);

    Bitmap<Bgra8888> Snapshot();
}
