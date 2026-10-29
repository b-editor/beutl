using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor;

// Mirror of SceneCompositor's per-frame layer snapshot + ShouldSkipLayer so export preflight excludes
// exactly the elements the compositor would: with any solo layer active only solo layers render;
// otherwise the per-target mute flag (video for the graphics pass, audio for the audio pass) decides.
// Layers are keyed by ZIndex, first one per ZIndex winning, matching GetLayerSnapshot. Shared by the
// root-scene walk (ExportSourceValidator) and the referenced-scene walk (ProxySourceEnumerator) so a
// muted/non-solo layer inside an embedded scene is skipped too.
internal readonly struct SceneLayerSkipModel
{
    private readonly Dictionary<int, TimelineLayer> _byZIndex;
    private readonly bool _hasSolo;

    private SceneLayerSkipModel(Dictionary<int, TimelineLayer> byZIndex, bool hasSolo)
    {
        _byZIndex = byZIndex;
        _hasSolo = hasSolo;
    }

    public static SceneLayerSkipModel Build(Scene scene)
    {
        var byZIndex = new Dictionary<int, TimelineLayer>(scene.Layers.Count);
        bool hasSolo = false;
        foreach (TimelineLayer layer in scene.Layers)
        {
            if (byZIndex.TryAdd(layer.ZIndex.Value, layer) && layer.IsSolo.Value)
                hasSolo = true;
        }

        return new SceneLayerSkipModel(byZIndex, hasSolo);
    }

    public bool ShouldSkip(int zIndex, CompositionTarget target, bool ignoreSolo = false)
    {
        _byZIndex.TryGetValue(zIndex, out TimelineLayer? layer);
        if (!ignoreSolo && _hasSolo && (layer is null || !layer.IsSolo.Value)) return true;
        if (layer is null) return false;
        return target == CompositionTarget.Graphics ? layer.IsVideoMuted.Value : layer.IsAudioMuted.Value;
    }

    // Both preflight entry points follow the same output and matte dependencies. A matte ignores solo
    // and output visibility, but still obeys enable/mute and is needed only while its consumer is active.
    public static IEnumerable<(Element Element, TimeRange Window)> EnumerateRenderableElements(
        Scene scene, CompositionTarget target, TimeRange? window)
    {
        SceneLayerSkipModel layers = Build(scene);
        var visiting = new HashSet<Element>();
        foreach (Element element in scene.Children)
            foreach (var item in Visit(element, window, false))
                yield return item;

        IEnumerable<(Element, TimeRange)> Visit(Element element, TimeRange? requested, bool matte)
        {
            if (!element.IsEnabled.Value
                || layers.ShouldSkip(element.ZIndex.Value, target, ignoreSolo: matte)
                || (target == CompositionTarget.Graphics && !matte && !element.IsVisibleInOutput.Value))
                yield break;

            TimeRange active = requested ?? element.Range;
            if (active.IsEmpty ? !element.Range.Contains(active.Start) : !element.Range.Intersects(active))
                yield break;
            if (!active.IsEmpty)
            {
                MediaTime start = active.Start > element.Start.Value ? active.Start : element.Start.Value;
                MediaTime end = active.End < element.Range.End ? active.End : element.Range.End;
                active = new TimeRange(start, end - start);
            }

            if (!visiting.Add(element)) yield break;
            try
            {
                yield return (element, active);
                if (target == CompositionTarget.Graphics && element.MatteMode.Value != MatteMode.None
                    && element.MatteSourceZIndex.Value is { } source)
                {
                    foreach (Element candidate in scene.Children)
                    {
                        if (candidate.ZIndex.Value != source) continue;
                        foreach (var item in Visit(candidate, active, true))
                            yield return item;
                    }
                }

                // Portals evaluated as matte sources import hidden/non-solo inputs too. Keep that
                // context and the consumer's window when following their layer ranges recursively.
                if (matte)
                {
                    foreach (PortalObject portal in element.Objects.OfType<PortalObject>())
                    {
                        if (!portal.IsEnabled.Value) continue;
                        int start = portal.ZIndex.Value + 1;
                        int end = portal.ZIndex.Value + portal.Count.Value;
                        foreach (Element candidate in scene.Children)
                        {
                            if (candidate.ZIndex.Value < start || candidate.ZIndex.Value > end) continue;
                            foreach (var item in Visit(candidate, active, true))
                                yield return item;
                        }
                    }
                }
            }
            finally
            {
                visiting.Remove(element);
            }
        }
    }
}
