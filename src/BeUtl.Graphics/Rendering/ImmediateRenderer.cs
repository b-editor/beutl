using BeUtl.Animation;
using BeUtl.Graphics;
using BeUtl.Threading;

namespace BeUtl.Rendering;

public class ImmediateRenderer : IRenderer
{
    private readonly SortedDictionary<int, ILayerContext> _objects = new();
    private readonly Canvas _graphics;
    private readonly FpsText _fpsText = new();
    private readonly InstanceClock _instanceClock = new();
    private TimeSpan _lastTimeSpan;

    public ImmediateRenderer(int width, int height)
    {
        _graphics = Dispatcher.Invoke(() => new Canvas(width, height));
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

    public ILayerContext? this[int index]
    {
        get => _objects.ContainsKey(index) ? _objects[index] : null;
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
        if (RenderInvalidated != null)
        {
            IRenderer.RenderResult result = await Dispatcher.InvokeAsync(() => Render(timeSpan));
            RenderInvalidated.Invoke(this, result);
            result.Bitmap.Dispose();
        }
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
                RenderCore(timeSpan);
            }

            IsRendering = false;
        }

        _lastTimeSpan = timeSpan;
        return new IRenderer.RenderResult(Graphics.GetBitmap());
    }

    protected virtual void RenderCore(TimeSpan timeSpan)
    {
        using (Graphics.PushCanvas())
        {
            Graphics.Clear();

            foreach (KeyValuePair<int, ILayerContext> item in _objects)
            {
                item.Value[timeSpan]?.Value?.Render(this);
            }
        }
    }

    protected void AddDirtyRect(Rect rect1)
    {

    }
}
