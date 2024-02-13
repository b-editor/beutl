using System.Runtime.InteropServices;

using Beutl.Animation;
using Beutl.Collections.Pooled;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Operation;
using Beutl.ProjectSystem;
using Beutl.Rendering;

namespace Beutl;

public sealed class SceneRenderer(Scene scene) : Renderer(scene.FrameSize.Width, scene.FrameSize.Height)
{
    private readonly List<Element> _entered = [];
    private readonly List<Element> _exited = [];
    private TimeSpan _lastTime = TimeSpan.MinValue;

    public List<Element> CurrentElements { get; } = [];

    public void Evaluate(TimeSpan timeSpan)
    {
        InternalClock.CurrentTime = timeSpan;
        SortLayers(timeSpan, out _);
        Span<Element> entered = CollectionsMarshal.AsSpan(_entered);
        Span<Element> exited = CollectionsMarshal.AsSpan(_exited);

        foreach (Element item in exited)
        {
            ExitSourceOperators(item);
        }

        foreach (Element item in entered)
        {
            EnterSourceOperators(item);
        }

        for (int i = 0; i < CurrentElements.Count; i++)
        {
            Element element = CurrentElements[i];
            using (PooledList<Renderable> list = element.Evaluate(EvaluationTarget.Graphics, Clock, this))
            {
                foreach (Renderable item in list.Span)
                {
                    if (item is Drawable drawable)
                    {
                        int actualIndex = (drawable as DrawableDecorator)?.OriginalZIndex ?? item.ZIndex;
                        RenderScene[actualIndex].Add(drawable);
                    }
                }
            }
        }

        _lastTime = timeSpan;
    }

    private static void EnterSourceOperators(Element element)
    {
        foreach (SourceOperator item in element.Operation.Children.GetMarshal().Value)
        {
            item.Enter();
        }
    }

    private static void ExitSourceOperators(Element element)
    {
        foreach (SourceOperator item in element.Operation.Children.GetMarshal().Value)
        {
            item.Exit();
        }
    }

    // Layersを振り分ける
    private void SortLayers(TimeSpan timeSpan, out TimeRange enterAffectsRange)
    {
        _entered.Clear();
        _exited.Clear();
        CurrentElements.Clear();
        TimeSpan enterStart = TimeSpan.MaxValue;
        TimeSpan enterEnd = TimeSpan.Zero;

        foreach (Element? item in scene.Children)
        {
            bool recent = InRange(item, _lastTime);
            bool current = InRange(item, timeSpan);

            if (current)
            {
                CurrentElements.OrderedAdd(item, x => x.ZIndex);
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

    protected override void RenderGraphicsCore()
    {
        Evaluate(Clock.CurrentTime);
        base.RenderGraphicsCore();
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        _entered.Clear();
        _exited.Clear();
        CurrentElements.Clear();
    }
}
