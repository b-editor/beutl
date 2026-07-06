using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Layer-level attribute writes (toggle every element at a zIndex, recolor a
/// <see cref="TimelineLayer"/>). Lazy-creates the <see cref="TimelineLayer"/>
/// when none exists yet — otherwise recoloring an unrecorded layer silently fails.
/// </summary>
public interface ILayerAttributeService
{
    /// <summary>Sets <see cref="Element.IsEnabled"/> = <paramref name="newEnabled"/>
    /// on every differing element at <paramref name="zIndex"/>. Returns true and
    /// commits <c>ChangeLayerEnabled</c> when at least one was mutated; otherwise
    /// false and no commit.</summary>
    bool SetEnabled(Scene scene, int zIndex, bool newEnabled);

    /// <summary>Sets <see cref="TimelineLayer.Color"/> for the layer at
    /// <paramref name="zIndex"/>, creating the model on demand. Returns true and
    /// commits <c>ChangeLayerColor</c> when the model was created or recolored;
    /// otherwise false and no commit.</summary>
    bool SetColor(Scene scene, int zIndex, Color color, string defaultName);

    /// <summary>Sets <see cref="TimelineLayer.IsLocked"/> for the layer at
    /// <paramref name="zIndex"/>, creating the model on demand. The editor treats
    /// a layer lock the same as locking every element at that zIndex.</summary>
    bool SetLocked(Scene scene, int zIndex, bool isLocked);

    /// <summary>Sets <see cref="TimelineLayer.IsAudioMuted"/> for the layer at
    /// <paramref name="zIndex"/>, creating the model on demand. Affects composition
    /// (playback and export), not the editor.</summary>
    bool SetAudioMuted(Scene scene, int zIndex, bool isMuted);

    /// <summary>Sets <see cref="TimelineLayer.IsVideoMuted"/> for the layer at
    /// <paramref name="zIndex"/>, creating the model on demand. Affects composition
    /// (playback and export), not the editor.</summary>
    bool SetVideoMuted(Scene scene, int zIndex, bool isMuted);

    /// <summary>Sets <see cref="TimelineLayer.IsSolo"/> for the layer at
    /// <paramref name="zIndex"/>, creating the model on demand. When any layer is
    /// soloed the compositor evaluates only soloed layers.</summary>
    bool SetSolo(Scene scene, int zIndex, bool isSolo);
}
