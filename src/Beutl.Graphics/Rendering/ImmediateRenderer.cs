using Beutl.Animation;
using Beutl.Audio;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Threading;

using SkiaSharp;

namespace Beutl.Rendering;

// 後で名前を変更
public class ImmediateRenderer : IRenderer, IImmediateCanvasFactory
{
    internal static readonly Dispatcher s_dispatcher = RenderThread.Dispatcher;
    private readonly ImmediateCanvas _immediateCanvas;
    private readonly SKSurface _surface;
    private readonly Audio.Audio _audio;
    private readonly FpsText _fpsText = new();
    private readonly InstanceClock _instanceClock = new();

    public ImmediateRenderer(int width, int height)
    {
        RenderScene = new RenderScene(new PixelSize(width, height));
        (_immediateCanvas, _surface) = Dispatcher.Invoke(() =>
        {
            var factory = (IImmediateCanvasFactory)this;
            SKSurface surface = factory.CreateRenderTarget(width, height);
            ImmediateCanvas canvas = factory.CreateCanvas(surface, false);
            return (canvas, surface);
        });
        _audio = new Audio.Audio(44100);
    }

    public ICanvas Graphics => _immediateCanvas;

    public Dispatcher Dispatcher => s_dispatcher;

    public bool IsDisposed { get; private set; }

    public bool IsGraphicsRendering { get; private set; }

    public bool IsAudioRendering { get; private set; }

    public bool DrawFps
    {
        get => _fpsText.DrawFps;
        set => _fpsText.DrawFps = value;
    }

    public IClock Clock => _instanceClock;

    public RenderScene RenderScene { get; }

    public ImmediateCanvas Canvas => _immediateCanvas;

    public Audio.Audio Audio => _audio;

    public event EventHandler<TimeSpan>? RenderInvalidated;

    public virtual void Dispose()
    {
        if (IsDisposed) return;

        _immediateCanvas.Dispose();
        _audio.Dispose();
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
        Dispatcher.VerifyAccess();
        if (!IsGraphicsRendering)
        {
            IsGraphicsRendering = true;
            _instanceClock.CurrentTime = timeSpan;
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
        Dispatcher.VerifyAccess();
        if (!IsGraphicsRendering && !IsAudioRendering)
        {
            IsGraphicsRendering = true;
            IsAudioRendering = true;
            _instanceClock.CurrentTime = timeSpan;
            _instanceClock.AudioStartTime = timeSpan;
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
        Dispatcher.VerifyAccess();
        return new ImmediateCanvas(surface, leaveOpen)
        {
            Factory = this
        };
    }

    SKSurface IImmediateCanvasFactory.CreateRenderTarget(int width, int height)
    {
        Dispatcher.VerifyAccess();
        GRContext grcontext = SharedGRContext.GetOrCreate();

        var surface = SKSurface.Create(
            grcontext,
            false,
            new SKImageInfo(width, height, SKColorType.Bgra8888/*, SKAlphaType.Unpremul*/));

        return surface;
    }
}
