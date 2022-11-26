using Beutl.Animation;
using Beutl.Audio;
using Beutl.Graphics;
using Beutl.Threading;

namespace Beutl.Rendering;

public class ImmediateRenderer : IRenderer
{
    private readonly SortedDictionary<int, ILayerContext> _objects = new();
    private readonly Canvas _graphics;
    private readonly FpsText _fpsText = new();
    private readonly InstanceClock _instanceClock = new();

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

    public IAudio Audio => throw new NotImplementedException();

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
            result.Bitmap.Dispose();
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
                RenderCore(timeSpan);
            }

            IsRendering = false;
        }

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

    void IRenderer.AddDirtyRect(Rect rect)
    {
    }

    public IRenderer.RenderResult RenderAudio(TimeSpan timeSpan)
    {
        throw new NotImplementedException();
    }

    public IRenderer.RenderResult Render(TimeSpan timeSpan)
    {
        throw new NotImplementedException();
    }
}
