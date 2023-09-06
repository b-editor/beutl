using System.Runtime.InteropServices;

using Beutl.Animation;
using Beutl.Collections.Pooled;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Operation;
using Beutl.ProjectSystem;
using Beutl.Rendering;

namespace Beutl;

internal sealed class SceneGraphicsEvaluator : IDisposable
{
    private readonly Scene _scene;
    private readonly IRenderer _renderer;
    private readonly List<Element> _entered = new();
    private readonly List<Element> _exited = new();
    private readonly List<Element> _current = new();
    private TimeSpan _lastTime = TimeSpan.MinValue;

    public SceneGraphicsEvaluator(Scene scene, IRenderer renderer)
    {
        _scene = scene;
        _renderer = renderer;
    }

    public List<Element> CurrentElements => _current;

    public void Evaluate()
    {
        IClock clock = _renderer.Clock;
        TimeSpan timeSpan = clock.CurrentTime;
        SortLayers(timeSpan, out _);
        Span<Element> entered = CollectionsMarshal.AsSpan(_entered);
        Span<Element> exited = CollectionsMarshal.AsSpan(_exited);

        foreach (Element item in exited)
        {
            ExitSourceOperators(item);
            RenderLayer layer = _renderer.RenderScene[item.ZIndex];
            layer.ClearAllNodeCache(_renderer.GetCacheContext());
        }

        foreach (Element item in entered)
        {
            EnterSourceOperators(item);
        }

        for (int i = 0; i < _current.Count; i++)
        {
            Element layer = _current[i];
            using (PooledList<Renderable> list = layer.Evaluate(EvaluationTarget.Graphics, clock, _renderer))
            {
                foreach (Renderable item in list.Span)
                {
                    if (item is Drawable drawable)
                    {
                        int actualIndex = (drawable as DrawableDecorator)?.OriginalZIndex ?? item.ZIndex;
                        _renderer.RenderScene[actualIndex].Add(drawable);
                    }
                }
            }
        }

        _lastTime = timeSpan;
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
        _current.Clear();
        TimeSpan enterStart = TimeSpan.MaxValue;
        TimeSpan enterEnd = TimeSpan.Zero;

        foreach (Element? item in _scene.Children)
        {
            bool recent = InRange(item, _lastTime);
            bool current = InRange(item, timeSpan);

            if (current)
            {
                _current.OrderedAdd(item, x => x.ZIndex);
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

    public void Dispose()
    {
        _entered.Clear();
        _exited.Clear();
        _current.Clear();
    }
}
