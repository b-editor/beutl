using BeUtl.Animation;
using BeUtl.Graphics;
using BeUtl.Threading;

namespace BeUtl.Rendering;

public class ImmediateRenderer : IRenderer
{
    private readonly SortedDictionary<int, ILayerScope> _objects = new();
    private readonly Canvas _graphics;
    private readonly FpsText _fpsText = new();

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

    public IClock Clock { get; protected set; } = ZeroClock.Instance;

    public ILayerScope? this[int index]
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

    public async void Invalidate()
    {
        if (RenderInvalidated != null)
        {
            IRenderer.RenderResult result = await Dispatcher.InvokeAsync(() => Render());
            RenderInvalidated.Invoke(this, result);
            result.Bitmap.Dispose();
        }
    }

    public IRenderer.RenderResult Render()
    {
        Dispatcher.VerifyAccess();
        if (!IsRendering)
        {
            IsRendering = true;
            using (_fpsText.StartRender(this))
            {
                RenderCore();
            }

            IsRendering = false;
        }

        return new IRenderer.RenderResult(Graphics.GetBitmap());
    }

    protected virtual void RenderCore()
    {
        using (Graphics.PushCanvas())
        {
            Graphics.Clear();

            foreach (KeyValuePair<int, ILayerScope> item in _objects)
            {
                for (int ii = item.Value.Count - 1; ii >= 0; ii--)
                {
                    IRenderable item2 = item.Value[ii];
                    if (item2.IsVisible)
                    {
                        item2.Render(this);
                    }
                }
            }
        }
    }

    protected void AddDirtyRect(Rect rect1)
    {

    }
}
