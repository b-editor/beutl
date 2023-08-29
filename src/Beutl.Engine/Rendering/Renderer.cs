using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Rendering.Cache;

using SkiaSharp;

namespace Beutl.Rendering;

public class Renderer : IRenderer
{
    private readonly ImmediateCanvas _immediateCanvas;
    private readonly SKSurface _surface;
    private readonly FpsText _fpsText = new();
    private readonly InstanceClock _instanceClock = new();
    private readonly RenderCacheContext _cacheContext = new();

    public Renderer(int width, int height)
    {
        FrameSize = new PixelSize(width, height);
        RenderScene = new RenderScene(FrameSize);
        (_immediateCanvas, _surface) = RenderThread.Dispatcher.Invoke(() =>
        {
            var factory = (IImmediateCanvasFactory)this;
            SKSurface? surface = factory.CreateRenderTarget(width, height)
                ?? throw new InvalidOperationException($"Could not create a canvas of this size. (width: {width}, height: {height})");

            ImmediateCanvas canvas = factory.CreateCanvas(surface, false);
            return (canvas, surface);
        });
    }

    ~Renderer()
    {
        if (!IsDisposed)
        {
            OnDispose(false);
            _immediateCanvas.Dispose();
            _cacheContext.Dispose();

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

    public IClock Clock => _instanceClock;

    public PixelSize FrameSize { get; }

    public RenderScene RenderScene { get; }

    public event EventHandler<TimeSpan>? RenderInvalidated;

    public void Dispose()
    {
        if (!IsDisposed)
        {
            OnDispose(true);
            _immediateCanvas.Dispose();
            _cacheContext.Dispose();
            GC.SuppressFinalize(this);

            IsDisposed = true;
        }
    }

    protected virtual void OnDispose(bool disposing)
    {
    }

    public void RaiseInvalidated(TimeSpan timeSpan)
    {
        if (!IsGraphicsRendering)
        {
            RenderInvalidated?.Invoke(this, timeSpan);
        }
    }

    public Bitmap<Bgra8888>? RenderGraphics(TimeSpan timeSpan)
    {
        RenderThread.Dispatcher.VerifyAccess();
        if (!IsGraphicsRendering)
        {
            try
            {
                IsGraphicsRendering = true;
                _instanceClock.CurrentTime = timeSpan;
                RenderScene.Clear();
                using (_fpsText.StartRender(_immediateCanvas))
                {
                    RenderGraphicsCore();
                }

                return _immediateCanvas.GetBitmap();
            }
            finally
            {
                IsGraphicsRendering = false;
            }
        }
        else
        {
            return default;
        }
    }

    protected virtual void RenderGraphicsCore()
    {
        RenderScene.Render(_immediateCanvas);
    }

    ImmediateCanvas IImmediateCanvasFactory.CreateCanvas(SKSurface surface, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(surface);
        RenderThread.Dispatcher.VerifyAccess();

        return new ImmediateCanvas(surface, leaveOpen)
        {
            Factory = this
        };
    }

    SKSurface? IImmediateCanvasFactory.CreateRenderTarget(int width, int height)
    {
        RenderThread.Dispatcher.VerifyAccess();
        GRContext? grcontext = SharedGRContext.GetOrCreate();
        SKSurface? surface;

        if (grcontext != null)
        {
            surface = SKSurface.Create(
                grcontext,
                false,
                new SKImageInfo(width, height, SKColorType.Bgra8888/*, SKAlphaType.Unpremul*/));
        }
        else
        {
            surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        }

        return surface;
    }

    public RenderCacheContext? GetCacheContext()
    {
        return _cacheContext;
    }
}
