using Beutl.Animation;
using Beutl.Audio;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Threading;

namespace Beutl.Rendering;

public class ImmediateRenderer : IRenderer
{
    private readonly SortedDictionary<int, ILayerContext> _objects = new();
    private readonly Canvas _graphics;
    private readonly Audio.Audio _audio;
    private readonly FpsText _fpsText = new();
    private readonly InstanceClock _instanceClock = new();
    private int _lastAudioTime = -1;

    public ImmediateRenderer(int width, int height)
    {
        _graphics = Dispatcher.Invoke(() => new Canvas(width, height));
        _audio = new Audio.Audio(44100);
    }

    public ICanvas Graphics => _graphics;

    public Dispatcher Dispatcher => DeferredRenderer.s_dispatcher;

    public bool IsDisposed { get; private set; }

    public bool IsRendering { get; private set; }

    public bool DrawFps
    {
        get => _fpsText.DrawFps;
        set => _fpsText.DrawFps = value;
    }

    public IClock Clock => _instanceClock;

    public IAudio Audio => _audio;

    public ILayerContext? this[int index]
    {
        get => _objects.TryGetValue(index, out ILayerContext? value) ? value : null;
        set
        {
            if (value != null)
            {
                _objects[index] = value;
            }
            else
            {
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
        if (RenderInvalidated != null && !IsRendering)
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
        if (!IsRendering)
        {
            IsRendering = true;
            _instanceClock.CurrentTime = timeSpan;
            using (_fpsText.StartRender(this))
            {
                RenderGraphicsCore(timeSpan);
            }

            IsRendering = false;
        }

        return new IRenderer.RenderResult(Graphics.GetBitmap());
    }

    protected virtual void RenderGraphicsCore(TimeSpan timeSpan)
    {
        using (Graphics.PushCanvas())
        {
            Graphics.Clear();

            foreach (KeyValuePair<int, ILayerContext> item in _objects)
            {
                item.Value.RenderGraphics(this, timeSpan);
            }
        }
    }

    protected virtual void RenderAudioCore(TimeSpan timeSpan)
    {
        int start = (int)Math.Floor(timeSpan.TotalSeconds);
        if (_lastAudioTime == start)
        {
            _audio.Clear();

            foreach (KeyValuePair<int, ILayerContext> item in _objects)
            {
                item.Value.RenderAudio(this, timeSpan);
            }

            _lastAudioTime = start;
        }
    }

    void IRenderer.AddDirtyRect(Rect rect)
    {
    }

    void IRenderer.AddDirtyRange(TimeRange timeRange)
    {

    }

    public IRenderer.RenderResult RenderAudio(TimeSpan timeSpan)
    {
        Dispatcher.VerifyAccess();
        if (!IsRendering)
        {
            IsRendering = true;
            _instanceClock.CurrentTime = timeSpan;
            RenderAudioCore(timeSpan);

            IsRendering = false;
        }

        return new IRenderer.RenderResult(Audio: Audio.GetPcm());
    }

    public IRenderer.RenderResult Render(TimeSpan timeSpan)
    {
        Dispatcher.VerifyAccess();
        if (!IsRendering)
        {
            IsRendering = true;
            _instanceClock.CurrentTime = timeSpan;
            using (_fpsText.StartRender(this))
            {
                RenderGraphicsCore(timeSpan);
                RenderAudioCore(timeSpan);
            }

            IsRendering = false;
        }

        return new IRenderer.RenderResult(Graphics.GetBitmap(), Audio.GetPcm());
    }
}
