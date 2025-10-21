using Beutl.Animation;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.Media.Pixel;

namespace Beutl.Graphics.Rendering;

public class Renderer : IRenderer
{
    private readonly ImmediateCanvas _immediateCanvas;
    private readonly RenderTarget _surface;
    private readonly FpsText _fpsText = new();

    public Renderer(int width, int height)
    {
        FrameSize = new PixelSize(width, height);
        RenderScene = new RenderScene(FrameSize);
        (_immediateCanvas, _surface) = RenderThread.Dispatcher.Invoke(() =>
        {
            RenderTarget surface = RenderTarget.Create(width, height)
                                    ?? throw new InvalidOperationException($"Could not create a canvas of this size. (width: {width}, height: {height})");

            var canvas = new ImmediateCanvas(surface);
            return (canvas, surface);
        });
    }

    ~Renderer()
    {
        if (!IsDisposed)
        {
            OnDispose(false);
            _immediateCanvas.Dispose();
            _surface.Dispose();
            RenderScene.ClearCache();
            RenderScene.Dispose();

            IsDisposed = true;
        }
    }

    public bool IsDisposed { get; private set; }

    public bool IsGraphicsRendering { get; private set; }

    public bool DrawFps
    {
        get => _fpsText.DrawFps;
        set => _fpsText.DrawFps = value;
    }

    public TimeSpan Time { get; internal set; }

    public PixelSize FrameSize { get; }

    public RenderScene RenderScene { get; }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            OnDispose(true);
            _immediateCanvas.Dispose();
            _surface.Dispose();
            RenderScene.ClearCache();
            RenderScene.Dispose();
            GC.SuppressFinalize(this);

            IsDisposed = true;
        }
    }

    public RenderNodeCacheContext GetCacheContext()
    {
        return RenderScene._cacheContext;
    }

    protected virtual void OnDispose(bool disposing)
    {
    }

    protected virtual void RenderGraphicsCore()
    {
        RenderScene.Render(_immediateCanvas);
    }

    public Drawable? HitTest(Point point)
    {
        RenderThread.Dispatcher.VerifyAccess();
        return RenderScene.HitTest(point);
    }

    public bool Render(TimeSpan time)
    {
        RenderThread.Dispatcher.VerifyAccess();
        if (!IsGraphicsRendering)
        {
            try
            {
                IsGraphicsRendering = true;
                Time = time;
                RenderScene.Clear();
                using (_fpsText.StartRender(_immediateCanvas))
                {
                    RenderGraphicsCore();
                }
            }
            finally
            {
                IsGraphicsRendering = false;
            }

            return true;
        }
        else
        {
            return false;
        }
    }

    public Bitmap<Bgra8888> Snapshot()
    {
        RenderThread.Dispatcher.VerifyAccess();
        return _surface.Snapshot();
    }

    public static ImmediateCanvas GetInternalCanvas(Renderer renderer)
    {
        return renderer._immediateCanvas;
    }
}
