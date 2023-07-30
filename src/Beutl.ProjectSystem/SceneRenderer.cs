using System.Runtime.InteropServices;

using Beutl.Media;
using Beutl.Operation;
using Beutl.ProjectSystem;
using Beutl.Rendering;

namespace Beutl;

internal sealed class SceneRenderer :
    Renderer
//DeferredRenderer
{
    private readonly Scene _scene;
    private readonly List<Element> _entered = new();
    private readonly List<Element> _exited = new();
    private readonly List<Element> _layers = new();
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
        Span<Element> layers = CollectionsMarshal.AsSpan(_layers);
        Span<Element> entered = CollectionsMarshal.AsSpan(_entered);
        Span<Element> exited = CollectionsMarshal.AsSpan(_exited);

        foreach (Element item in exited)
        {
            ExitSourceOperators(item);
            RenderLayer layer = RenderScene[item.ZIndex];
            layer.Clear();
            layer.ClearAllNodeCache(GetCacheContext());
        }

        foreach (Element item in entered)
        {
            EnterSourceOperators(item);
        }

        foreach (Element layer in layers)
        {
            layer.Evaluate(this);
        }

        base.RenderGraphicsCore();
        _recentTime = timeSpan;
    }

    private static void EnterSourceOperators(Element layer)
    {
        foreach (SourceOperator item in layer.Operation.Children.GetMarshal().Value)
        {
            item.Enter();
        }
    }

    private static void ExitSourceOperators(Element layer)
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

        foreach (Element? item in _scene.Children)
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
    private static bool InRange(Element item, TimeSpan ts)
    {
        return item.Start <= ts && ts < item.Length + item.Start;
    }
}
