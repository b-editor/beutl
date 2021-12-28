using BEditorNext.Collections;
using BEditorNext.Graphics;
using BEditorNext.ProjectSystem;
using BEditorNext.Rendering;
using BEditorNext.Threading;

namespace BEditorNext;

internal class SceneRenderer : IRenderer
{
    internal static readonly Dispatcher s_dispatcher = Dispatcher.Spawn();
    private readonly Scene _scene;
    private readonly RenderableList _renderables = new();

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

    public event EventHandler<IRenderer.RenderResult>? RenderRequested;

    public void Dispose()
    {
        if (IsDisposed) return;

        Graphics?.Dispose();

        IsDisposed = true;
    }

    public async void ForceRender()
    {
        IRenderer.RenderResult result = await Dispatcher.InvokeAsync(() => Render());
        RenderRequested?.Invoke(this, result);
        result.Bitmap.Dispose();
    }

    public IRenderer.RenderResult Render()
    {
        Dispatcher.VerifyAccess();
        if (!IsRendering)
        {
            Graphics.Clear();
            TimeSpan ts = FrameNumber;
            List<SceneLayer> layers = FilterLayers(_scene, ts);
            var args = new OperationRenderArgs(ts, this, _renderables);

            for (int i = 0; i < layers.Count; i++)
            {
                SceneLayer item = layers[i];

                if (item.IsEnabled)
                {
                    ProcessClip(item, args);
                }
            }
        }

        return new IRenderer.RenderResult(Graphics.GetBitmap());
    }

    private void ProcessClip(SceneLayer layer, in OperationRenderArgs args)
    {
        _renderables.Clear();
        IElementList list = layer.Children;

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is RenderOperation op && op.IsEnabled)
            {
                UpdateProperty(op, args.CurrentTime);
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

    private static void UpdateProperty(RenderOperation op, TimeSpan timeSpan)
    {
        int length = op.Setters.Count;
        for (int i = 0; i < length; i++)
        {
            ISetter item = op.Setters[i];

            if (item is IAnimatableSetter anmSetter)
            {
                anmSetter.SetProperty(timeSpan);
            }
            else
            {
                item.SetProperty();
            }
        }
    }

    private static List<SceneLayer> FilterLayers(Scene scene, TimeSpan ts)
    {
        var list = new List<SceneLayer>();
        int length = scene.Children.Count;

        for (int i = 0; i < length; i++)
        {
            if (scene.Children[i] is SceneLayer item &&
                item.Start <= ts &&
                ts < item.Length + item.Start &&
                item.Layer != -1)
            {
                list.Add(item);
            }
        }

        list.Sort((x, y) => x.Layer - y.Layer);

        return list;
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
