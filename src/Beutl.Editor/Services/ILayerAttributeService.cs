using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Layer-level attribute writes (toggle every element at a zIndex,
/// recolor a <see cref="TimelineLayer"/>). The lazy-create of
/// <see cref="TimelineLayer"/> when none exists yet is the main piece
/// of logic worth testing in isolation — without it the existing color
/// picker on an unrecorded layer would silently fail.
/// </summary>
public interface ILayerAttributeService
{
    /// <summary>Sets <see cref="Element.IsEnabled"/> = <paramref name="newEnabled"/> on
    /// every element at <paramref name="zIndex"/> whose current state differs.
    /// Returns true and commits <c>ChangeLayerEnabled</c> when at least one element
    /// was mutated; returns false and commits nothing when every element already
    /// matched.</summary>
    bool SetEnabled(Scene scene, int zIndex, bool newEnabled);

    /// <summary>Sets the <see cref="TimelineLayer.Color"/> for the layer at
    /// <paramref name="zIndex"/>, creating the model on demand if none exists yet.
    /// Returns true and commits <c>ChangeLayerColor</c> when the model was created
    /// or its color was changed; returns false and commits nothing when the existing
    /// model already had the target color.</summary>
    bool SetColor(Scene scene, int zIndex, Color color, string defaultName);
}
