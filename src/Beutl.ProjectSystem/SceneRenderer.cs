using System.Runtime.InteropServices;

using Beutl.Operation;
using Beutl.ProjectSystem;
using Beutl.Rendering;

namespace Beutl;

internal sealed class SceneRenderer :
    ImmediateRenderer
//DeferredRenderer
{
    private readonly Scene _scene;
    private readonly List<Layer> _begin = new();
    private readonly List<Layer> _end = new();
    private readonly List<Layer> _layers = new();
    private readonly List<Renderable> _unhandleds = new();
    private TimeSpan _recentTime = TimeSpan.MinValue;

    public SceneRenderer(Scene scene, int width, int height)
        : base(width, height)
    {
        _scene = scene;
    }

    public TimeSpan FrameNumber => _scene.CurrentFrame;

    public TimeSpan CurrentTime { get; private set; }

    protected override void RenderGraphicsCore()
    {
        var timeSpan = Clock.CurrentTime;
        CurrentTime = timeSpan;
        SortLayers(timeSpan);
        Span<Layer> layers = CollectionsMarshal.AsSpan(_layers);
        Span<Layer> begin = CollectionsMarshal.AsSpan(_begin);
        Span<Layer> end = CollectionsMarshal.AsSpan(_end);
        _unhandleds.Clear();

        foreach (Layer item in end)
        {
            ExitSourceOperators(item);
        }

        foreach (Layer item in begin)
        {
            EnterSourceOperators(item);
        }

        foreach (Layer layer in layers)
        {
            if (layer.UseNode)
            {
                layer.NodeTree.Evaluate(this, layer);
            }
            else
            {
                layer.Operation.Evaluate(this, layer, _unhandleds);
            }
        }

        base.RenderGraphicsCore();
        _recentTime = timeSpan;
    }

    private static void EnterSourceOperators(Layer layer)
    {
        foreach (SourceOperator item in layer.Operation.Children.GetMarshal().Value)
        {
            item.Enter();
        }
    }

    private static void ExitSourceOperators(Layer layer)
    {
        foreach (SourceOperator item in layer.Operation.Children.GetMarshal().Value)
        {
            item.Exit();
        }
    }

    // Layersを振り分ける
    private void SortLayers(TimeSpan timeSpan)
    {
        _begin.Clear();
        _end.Clear();
        _layers.Clear();
        // Todo: 'public Layers Children'はソート済みにしたい
        foreach (Layer? item in _scene.Children)
        {
            bool recent = InRange(item, _recentTime);
            bool current = InRange(item, timeSpan);

            if (current)
            {
                _layers.OrderedAdd(item, x => x.ZIndex);
            }

            if (!recent && current)
            {
                // _recentTimeの範囲外でcurrntTimeの範囲内
                _begin.OrderedAdd(item, x => x.ZIndex);
            }
            else if (recent && !current)
            {
                // _recentTimeの範囲内でcurrntTimeの範囲外
                _end.OrderedAdd(item, x => x.ZIndex);
            }
        }
    }


    // itemがtsの範囲内かを確かめます
    private static bool InRange(Layer item, TimeSpan ts)
    {
        return item.Start <= ts && ts < item.Length + item.Start;
    }
}
