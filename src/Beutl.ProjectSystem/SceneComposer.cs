using System.Runtime.InteropServices;

using Beutl.Animation;
using Beutl.Audio;
using Beutl.Collections.Pooled;
using Beutl.Media;
using Beutl.Operation;
using Beutl.ProjectSystem;
using Beutl.Rendering;

namespace Beutl;

public sealed class SceneComposer(Scene scene, IRenderer renderer) : Composer
{
    private readonly List<Element> _entered = [];
    private readonly List<Element> _exited = [];
    private readonly List<Element> _current = [];
    private TimeRange _lastTime = new(TimeSpan.MinValue, default);

    protected override void ComposeCore(Audio.Audio audio)
    {
        base.ComposeCore(audio);
        audio.Clear();

        IClock clock = Clock;
        var timeSpan = new TimeRange(clock.AudioStartTime, TimeSpan.FromSeconds(1));
        SortLayers(timeSpan, out _);
        Span<Element> elements = CollectionsMarshal.AsSpan(_current);
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

        foreach (Element element in elements)
        {
            using (PooledList<Renderable> list = element.Evaluate(EvaluationTarget.Audio, clock, renderer))
            {
                foreach (Renderable item in list.Span)
                {
                    if (item is Sound sound)
                    {
                        sound.Render(audio);
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
    private void SortLayers(TimeRange timeSpan, out TimeRange enterAffectsRange)
    {
        _entered.Clear();
        _exited.Clear();
        _current.Clear();
        TimeSpan enterStart = TimeSpan.MaxValue;
        TimeSpan enterEnd = TimeSpan.Zero;

        foreach (Element? item in scene.Children)
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
    private static bool InRange(Element item, TimeRange ts)
    {
        return item.Range.Intersects(ts);
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        _entered.Clear();
        _exited.Clear();
        _current.Clear();
    }
}
