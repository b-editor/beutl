using System.Runtime.InteropServices;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Collections.Pooled;
using Beutl.Engine;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl;

public sealed class SceneComposer(Scene scene) : Composer
{
    private readonly List<Element> _current = [];

    protected override void ComposeCore(TimeRange timeRange)
    {
        ClearSounds();
        SortLayers(timeRange);

        using var list = new PooledList<EngineObject>();
        foreach (Element element in CollectionsMarshal.AsSpan(_current))
        {
            list.Clear();
            element.CollectObjects(EvaluationTarget.Audio, list);
            foreach (EngineObject item in list.Span)
            {
                if (item is Sound sound)
                {
                    AddSound(sound);
                }
            }
        }

        base.ComposeCore(timeRange);
    }

    // Layersを振り分ける
    private void SortLayers(TimeRange timeSpan)
    {
        _current.Clear();

        foreach (Element? item in scene.Children)
        {
            if (InRange(item, timeSpan))
            {
                _current.OrderedAdd(item, x => x.ZIndex);
            }
        }
    }

    // itemがtsの範囲内かを確かめます
    private static bool InRange(Element item, TimeRange ts)
    {
        return item.Range.Intersects(ts);
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        _current.Clear();
    }
}
