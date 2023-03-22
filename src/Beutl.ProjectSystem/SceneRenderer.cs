using System.Runtime.InteropServices;

using Beutl.Media;
using Beutl.Operation;
using Beutl.ProjectSystem;
using Beutl.Rendering;

namespace Beutl;

internal sealed class SceneRenderer :
    ImmediateRenderer
//DeferredRenderer
{
    private readonly Scene _scene;
    private readonly List<Layer> _entered = new();
    private readonly List<Layer> _exited = new();
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
        SortLayers(timeSpan, out _);
        Span<Layer> layers = CollectionsMarshal.AsSpan(_layers);
        Span<Layer> entered = CollectionsMarshal.AsSpan(_entered);
        Span<Layer> exited = CollectionsMarshal.AsSpan(_exited);
        _unhandleds.Clear();

        foreach (Layer item in exited)
        {
            ExitSourceOperators(item);
        }

        foreach (Layer item in entered)
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
    private void SortLayers(TimeSpan timeSpan, out TimeRange enterAffectsRange)
    {
        _entered.Clear();
        _exited.Clear();
        _layers.Clear();
        TimeSpan enterStart = TimeSpan.MaxValue;
        TimeSpan enterEnd = TimeSpan.Zero;

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
                _entered.OrderedAdd(item, x => x.ZIndex);
                if (item.Start < enterStart)
                    enterStart = item.Start;

                TimeSpan end = item.Range.End;
                if (enterEnd < end)
                    enterEnd = end;
            }
            else if (recent && !current)
            {
                // _recentTimeの範囲内でcurrntTimeの範囲外
                _exited.OrderedAdd(item, x => x.ZIndex);
            }
        }

        enterAffectsRange = TimeRange.FromRange(enterStart, enterEnd);
    }


    // itemがtsの範囲内かを確かめます
    private static bool InRange(Layer item, TimeSpan ts)
    {
        return item.Start <= ts && ts < item.Length + item.Start;
    }
}
