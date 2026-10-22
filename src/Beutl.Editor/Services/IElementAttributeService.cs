using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Single-element attribute writes (boolean flags, accent color, etc.).
/// Distinct from <see cref="IElementStructureService"/>: one property on one
/// <see cref="Element"/>, no file IO or scene-graph traversal. New attributes
/// belong here, not on the structure service.
/// </summary>
public interface IElementAttributeService
{
    void SetEnabled(Element element, bool isEnabled);

    void SetAccentColor(Element element, Color color);

    void SetLocked(Element element, bool isLocked);

    /// <summary>
    /// Renames <paramref name="element"/>. The host commits the rename as one history entry.
    /// </summary>
    void SetName(Element element, string name);

    /// <summary>
    /// Gives the transition at <paramref name="edge"/> of <paramref name="element"/> the given type. When the
    /// edge has no transition yet, adds one: centred on the cut and split between both elements when another
    /// element meets this edge, or on this element alone when none does. The host commits the change as one
    /// history entry.
    /// </summary>
    void ApplyTransition(Element element, ElementEdge edge, Type transitionType);

    /// <summary>
    /// Removes the transition at <paramref name="edge"/> of <paramref name="element"/>, from both elements
    /// when another element meets this edge. The host commits the change as one history entry.
    /// </summary>
    void RemoveTransition(Element element, ElementEdge edge);

    /// <summary>
    /// Sets the duration <paramref name="element"/>'s side adds to the transition at <paramref name="edge"/>.
    /// The host commits the change as one history entry.
    /// </summary>
    void SetTransitionDuration(Element element, ElementEdge edge, MediaTime duration);
}
