using System.Runtime.InteropServices;

using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Collections.Pooled;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl;

public sealed class SceneComposer(Scene scene, IRenderer renderer) : Composer
{
    private readonly List<Element> _entered = [];
    private readonly List<Element> _exited = [];
    private readonly List<Element> _current = [];
    private TimeRange _lastTime = new(TimeSpan.MinValue, default);

    protected override void ComposeCore(TimeRange timeRange)
    {
        ClearSounds();
        SortLayers(timeRange, out _);
        Span<Element> elements = CollectionsMarshal.AsSpan(_current);
        Span<Element> entered = CollectionsMarshal.AsSpan(_entered);
        Span<Element> exited = CollectionsMarshal.AsSpan(_exited);

        foreach (Element item in exited)
        {
            ExitObjects(item);
        }

        foreach (Element item in entered)
        {
            EnterObjects(item);
        }

        foreach (Element element in elements)
        {
            using PooledList<EngineObject> list = element.Evaluate(EvaluationTarget.Audio, renderer);
            foreach (EngineObject item in list.Span)
            {
                if (item is Sound sound)
                {
                    AddSound(sound);
                }
            }
        }

        _lastTime = timeRange;
        base.ComposeCore(timeRange);
    }

    private static void EnterObjects(Element element)
    {
        foreach (EngineObject item in element.Objects.GetMarshal().Value)
        {
            if (item is IFlowOperator flowOperator)
            {
                flowOperator.EnterFlow();
            }
        }
    }

    private static void ExitObjects(Element element)
    {
        foreach (EngineObject item in element.Objects.GetMarshal().Value)
        {
            if (item is IFlowOperator flowOperator)
            {
                flowOperator.ExitFlow();
            }
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
