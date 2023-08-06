using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Rendering.Cache;
using Beutl.Threading;

using SkiaSharp;

namespace Beutl.Rendering;

public class Renderer : IRenderer
{
    private readonly ImmediateCanvas _immediateCanvas;
    private readonly SKSurface _surface;
    private readonly Audio.Audio _audio;
    private readonly FpsText _fpsText = new();
    private readonly InstanceClock _instanceClock = new();
    private readonly RenderCacheContext _cacheContext = new();

    public Renderer(int width, int height)
    {
        FrameSize = new PixelSize(width, height);
        SampleRate = 44100;
        RenderScene = new RenderScene(FrameSize);
        (_immediateCanvas, _surface) = RenderThread.Dispatcher.Invoke(() =>
        {
            var factory = (IImmediateCanvasFactory)this;
            SKSurface surface = factory.CreateRenderTarget(width, height);
            ImmediateCanvas canvas = factory.CreateCanvas(surface, false);
            return (canvas, surface);
        });
        _audio = new Audio.Audio(44100);
    }

    public bool IsDisposed { get; private set; }

    public bool IsGraphicsRendering { get; private set; }

    public bool IsAudioRendering { get; private set; }

    public bool DrawFps
    {
        get => _fpsText.DrawFps;
        set => _fpsText.DrawFps = value;
    }

    public IClock Clock => _instanceClock;

    public PixelSize FrameSize { get; }

    public int SampleRate { get; }

    public RenderScene RenderScene { get; }

    public event EventHandler<TimeSpan>? RenderInvalidated;

    public virtual void Dispose()
    {
        if (IsDisposed) return;

        _immediateCanvas.Dispose();
        _audio.Dispose();
        _cacheContext.Dispose();
        GC.SuppressFinalize(this);

        IsDisposed = true;
    }

    public void RaiseInvalidated(TimeSpan timeSpan)
    {
        if (!IsGraphicsRendering)
        {
            RenderInvalidated?.Invoke(this, timeSpan);
        }
    }

    public IRenderer.RenderResult RenderGraphics(TimeSpan timeSpan)
    {
        RenderThread.Dispatcher.VerifyAccess();
        if (!IsGraphicsRendering)
        {
            IsGraphicsRendering = true;
            _instanceClock.CurrentTime = timeSpan;
            RenderScene.Clear();
            using (_fpsText.StartRender(_immediateCanvas))
            {
                RenderGraphicsCore();
            }

            IsGraphicsRendering = false;
            return new IRenderer.RenderResult(_immediateCanvas.GetBitmap());
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

    protected virtual void RenderAudioCore()
    {
        RenderScene.Render(_audio);
    }

    public IRenderer.RenderResult RenderAudio(TimeSpan timeSpan)
    {
        if (!IsAudioRendering)
        {
            IsAudioRendering = true;
            _instanceClock.AudioStartTime = timeSpan;
            RenderAudioCore();

            IsAudioRendering = false;
            return new IRenderer.RenderResult(Audio: _audio.GetPcm());
        }
        else
        {
            return default;
        }
    }

    public IRenderer.RenderResult Render(TimeSpan timeSpan)
    {
        RenderThread.Dispatcher.VerifyAccess();
        if (!IsGraphicsRendering && !IsAudioRendering)
        {
            IsGraphicsRendering = true;
            IsAudioRendering = true;
            _instanceClock.CurrentTime = timeSpan;
            _instanceClock.AudioStartTime = timeSpan;
            RenderScene.Clear();
            using (_fpsText.StartRender(_immediateCanvas))
            {
                RenderGraphicsCore();
                RenderAudioCore();
            }

            IsGraphicsRendering = false;
            IsAudioRendering = false;
            return new IRenderer.RenderResult(_immediateCanvas.GetBitmap(), _audio.GetPcm());
        }
        else
        {
            return default;
        }
    }

    ImmediateCanvas IImmediateCanvasFactory.CreateCanvas(SKSurface surface, bool leaveOpen)
    {
        RenderThread.Dispatcher.VerifyAccess();
        return new ImmediateCanvas(surface, leaveOpen)
        {
            Factory = this
        };
    }

    SKSurface IImmediateCanvasFactory.CreateRenderTarget(int width, int height)
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
