using Beutl.Animation;
using Beutl.Audio;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Threading;

namespace Beutl.Rendering;

public class ImmediateRenderer : IRenderer
{
    internal static readonly Dispatcher s_dispatcher = Dispatcher.Spawn();
    private readonly SortedDictionary<int, IRenderLayer> _objects = new();
    private readonly Canvas _graphics;
    private readonly Audio.Audio _audio;
    private readonly FpsText _fpsText = new();
    private readonly InstanceClock _instanceClock = new();

    public ImmediateRenderer(int width, int height)
    {
        _graphics = Dispatcher.Invoke(() => new Canvas(width, height));
        _audio = new Audio.Audio(44100);
    }

    public ICanvas Graphics => _graphics;

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

    public IAudio Audio => _audio;

    public IRenderLayer? this[int index]
    {
        get => _objects.TryGetValue(index, out IRenderLayer? value) ? value : null;
        set
        {
            if (value != null)
            {
                value.AttachToRenderer(this);
                _objects[index] = value;
            }
            else if (_objects.TryGetValue(index, out IRenderLayer? oldLayer))
            {
                oldLayer.DetachFromRenderer();
                _objects.Remove(index);
            }
        }
    }

    public event EventHandler<IRenderer.RenderResult>? RenderInvalidated;

    public virtual void Dispose()
    {
        if (IsDisposed) return;

        Graphics?.Dispose();
        GC.SuppressFinalize(this);

        IsDisposed = true;
    }

    public async void Invalidate(TimeSpan timeSpan)
    {
        if (RenderInvalidated != null && !IsGraphicsRendering)
        {
            IRenderer.RenderResult result = await Dispatcher.InvokeAsync(() => RenderGraphics(timeSpan));
            RenderInvalidated?.Invoke(this, result);
            result.Bitmap?.Dispose();
            result.Audio?.Dispose();
        }
    }

    public IRenderer.RenderResult RenderGraphics(TimeSpan timeSpan)
    {
        Dispatcher.VerifyAccess();
        if (!IsGraphicsRendering)
        {
            IsGraphicsRendering = true;
            _instanceClock.CurrentTime = timeSpan;
            using (_fpsText.StartRender(this))
            {
                RenderGraphicsCore();
            }

            IsGraphicsRendering = false;
            return new IRenderer.RenderResult(Graphics.GetBitmap());
        }
        else
        {
            return default;
        }
    }

    protected virtual void RenderGraphicsCore()
    {
        using (Graphics.PushCanvas())
        {
            Graphics.Clear();

            foreach (KeyValuePair<int, IRenderLayer> item in _objects)
            {
                item.Value.RenderGraphics();
            }
        }
    }

    protected virtual void RenderAudioCore()
    {
        _audio.Clear();

        foreach (KeyValuePair<int, IRenderLayer> item in _objects)
        {
            item.Value.RenderAudio();
        }
    }

    //void IRenderer.AddDirtyRect(Rect rect)
    //{
    //}

    //void IRenderer.AddDirtyRange(TimeRange timeRange)
    //{

    //}

    public IRenderer.RenderResult RenderAudio(TimeSpan timeSpan)
    {
        if (!IsAudioRendering)
        {
            IsAudioRendering = true;
            _instanceClock.AudioStartTime = timeSpan;
            RenderAudioCore();

            IsAudioRendering = false;
            return new IRenderer.RenderResult(Audio: Audio.GetPcm());
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
            using (_fpsText.StartRender(this))
            {
                RenderGraphicsCore();
                RenderAudioCore();
            }

            IsGraphicsRendering = false;
            IsAudioRendering = false;
            return new IRenderer.RenderResult(Graphics.GetBitmap(), Audio.GetPcm());
        }
        else
        {
            return default;
        }
    }

    //public void AddDirty(IRenderable renderable)
    //{
    //}
}
