using BeUtl.Collections;
using BeUtl.Graphics;
using BeUtl.ProjectSystem;
using BeUtl.Rendering;
using BeUtl.Threading;

namespace BeUtl;

internal class SceneRenderer : IRenderer
{
    internal static readonly Dispatcher s_dispatcher = Dispatcher.Spawn();
    private readonly Scene _scene;
    private readonly RenderableList _renderables = new();
    private List<Layer>? _cache;

    public SceneRenderer(Scene scene, int width, int height)
    {
        _scene = scene;
        Graphics = s_dispatcher.Invoke(() => new Canvas(width, height));
    }

    public ICanvas Graphics { get; }

    public Dispatcher Dispatcher => s_dispatcher;

    public TimeSpan FrameNumber => _scene.CurrentFrame;

    public bool IsDisposed { get; private set; }

    public bool IsRendering { get; private set; }

    public event EventHandler<IRenderer.RenderResult>? RenderInvalidated;

    public void Dispose()
    {
        if (IsDisposed) return;

        Graphics?.Dispose();
        _cache = null;

        IsDisposed = true;
    }

    public IRenderer.RenderResult Render()
    {
        Dispatcher.VerifyAccess();
        if (!IsRendering)
        {
            Graphics.Clear();
            TimeSpan ts = FrameNumber;
            List<Layer> layers = FilterAndSortLayers(ts);
            var args = new OperationRenderArgs(ts, this, _renderables);

            for (int i = 0; i < layers.Count; i++)
            {
                Layer item = layers[i];

                if (item.IsEnabled)
                {
                    ProcessLayer(item, args);
                }
            }
        }

        return new IRenderer.RenderResult(Graphics.GetBitmap());
    }

    private void ProcessLayer(Layer layer, in OperationRenderArgs args)
    {
        _renderables.Clear();
        IElementList list = layer.Children;

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is LayerOperation op && op.IsEnabled)
            {
                op.ApplySetters(args);
                op.Render(args);
            }
        }

        for (int i = 0; i < _renderables.Count; i++)
        {
            IRenderable renderable = _renderables[i];
            if (!renderable.IsDisposed)
            {
                renderable.Render(this);
                renderable.Dispose();
            }
        }

        _renderables.Clear();
    }

    private List<Layer> FilterAndSortLayers(TimeSpan ts)
    {
        if (_cache == null)
        {
            _cache = new List<Layer>();
        }
        else
        {
            _cache.Clear();
        }
        int length = _scene.Children.Count;
        IElementList children = _scene.Children;

        for (int i = 0; i < length; i++)
        {
            if (children[i] is Layer item &&
                item.Start <= ts &&
                ts < item.Length + item.Start &&
                item.ZIndex >= 0)
            {
                _cache.Add(item);
            }
        }

        _cache.Sort((x, y) => x.ZIndex - y.ZIndex);

        return _cache;
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

    //private static int ToFrameNumber(TimeSpan tp, int rate)
    //{
    //    return (int)(tp.TotalSeconds * rate);
    //}

    //private static int TicksPerFrame(int rate)
    //{
    //    return 10000000 / rate;
    //}

    //private static TimeSpan ToTimeSpan(int f, int rate)
    //{
    //    return TimeSpan.FromSeconds(f / (double)rate);
    //}
}
