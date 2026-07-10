using Beutl.Composition;
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
            if (byZIndex.TryAdd(layer.ZIndex, layer) && layer.IsSolo)
                hasSolo = true;
        }

        return new SceneLayerSkipModel(byZIndex, hasSolo);
    }

    public bool ShouldSkip(int zIndex, CompositionTarget target)
    {
        _byZIndex.TryGetValue(zIndex, out TimelineLayer? layer);
        if (_hasSolo && (layer is null || !layer.IsSolo)) return true;
        if (layer is null) return false;
        return target == CompositionTarget.Graphics ? layer.IsVideoMuted : layer.IsAudioMuted;
    }
}
