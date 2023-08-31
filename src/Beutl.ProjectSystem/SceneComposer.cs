using System.Runtime.InteropServices;

using Beutl.Animation;
using Beutl.Audio;
using Beutl.Collections.Pooled;
using Beutl.Media;
using Beutl.Operation;
using Beutl.ProjectSystem;
using Beutl.Rendering;

namespace Beutl;

public sealed class SceneComposer : Composer
{
    private readonly Scene _scene;
    private readonly List<Element> _entered = new();
    private readonly List<Element> _exited = new();
    private readonly List<Element> _current = new();
    private TimeRange _lastTime = new(TimeSpan.MinValue, default);

    public SceneComposer(Scene scene)
    {
        _scene = scene;
    }

    protected override void ComposeCore(Audio.Audio audio)
    {
        base.ComposeCore(audio);
        audio.Clear();

        IClock clock = Clock;
        var timeSpan = new TimeRange(clock.AudioStartTime, TimeSpan.FromSeconds(1));
        SortLayers(timeSpan, out _);
        Span<Element> layers = CollectionsMarshal.AsSpan(_current);
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

        foreach (Element layer in layers)
        {
            using (PooledList<Renderable> list = layer.Evaluate(EvaluationTarget.Audio, clock, _scene.Renderer))
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
    private void SortLayers(TimeRange timeSpan, out TimeRange enterAffectsRange)
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
