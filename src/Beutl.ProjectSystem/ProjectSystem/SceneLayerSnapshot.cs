using Beutl.Composition;

namespace Beutl.ProjectSystem;

// The solo/mute rule shared by rendering and export preflight: with any solo layer active only solo layers render,
// unless the caller ignores solo, as a matte source does; otherwise the per-target mute flag (video for the
// graphics pass, audio for the audio pass) decides. Layers are keyed by ZIndex, the first one per ZIndex winning.
internal sealed class SceneLayerSnapshot
{
    private readonly Dictionary<int, TimelineLayer> _byZIndex;
    private readonly bool _hasSolo;

    private SceneLayerSnapshot(Dictionary<int, TimelineLayer> byZIndex, bool hasSolo)
    {
        _byZIndex = byZIndex;
        _hasSolo = hasSolo;
    }

    public static SceneLayerSnapshot Build(Scene scene)
    {
        var byZIndex = new Dictionary<int, TimelineLayer>(scene.Layers.Count);
        bool hasSolo = false;
        foreach (TimelineLayer layer in scene.Layers)
        {
            if (byZIndex.TryAdd(layer.ZIndex.Value, layer) && layer.IsSolo.Value)
                hasSolo = true;
        }

        return new SceneLayerSnapshot(byZIndex, hasSolo);
    }

    // A layer without a TimelineLayer model cannot be soloed, so it is excluded under solo mode.
    // Mute is independent per target (audio vs video).
    public bool ShouldSkip(int zIndex, CompositionTarget target, bool ignoreSolo = false)
    {
        _byZIndex.TryGetValue(zIndex, out TimelineLayer? layer);
        if (!ignoreSolo && _hasSolo && (layer is null || !layer.IsSolo.Value)) return true;
        if (layer is null) return false;
        return target == CompositionTarget.Graphics ? layer.IsVideoMuted.Value : layer.IsAudioMuted.Value;
    }
}
