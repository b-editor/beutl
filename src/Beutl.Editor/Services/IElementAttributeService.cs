using Beutl.Graphics.Transitions;
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
    /// Renames <paramref name="element"/>. The host commits the rename as one history entry; this default,
    /// kept for replacements written before the member existed, only writes the name.
    /// </summary>
    void SetName(Element element, string name)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.Name = name;
    }

    /// <summary>
    /// Gives the transition at <paramref name="edge"/> of <paramref name="element"/> the given type. When the
    /// edge has no transition yet, adds one: centred on the cut and split between both elements when another
    /// element meets this edge, or on this element alone when none does. The host commits the change as one
    /// history entry; this default, kept for replacements written before the member existed, only writes
    /// this element's side.
    /// </summary>
    void ApplyTransition(Element element, ElementEdge edge, Type transitionType)
    {
        ArgumentNullException.ThrowIfNull(element);
        ClipTransition? current = ElementTransitionEdits.GetTransition(element, edge);
        ClipTransition transition = current == null
            ? ElementTransitionEdits.CreateTransition(transitionType, ClipTransition.DefaultDuration)
            : current.GetType() == transitionType
                ? current
                : ElementTransitionEdits.ChangeType(current, transitionType);
        ElementTransitionEdits.SetTransition(element, edge, transition);
    }

    /// <summary>
    /// Removes the transition at <paramref name="edge"/> of <paramref name="element"/>, from both elements
    /// when another element meets this edge. The host commits the change as one history entry; this default,
    /// kept for replacements written before the member existed, only clears this element's side.
    /// </summary>
    void RemoveTransition(Element element, ElementEdge edge)
    {
        ArgumentNullException.ThrowIfNull(element);
        ElementTransitionEdits.SetTransition(element, edge, null);
    }

    /// <summary>
    /// Sets the duration <paramref name="element"/>'s side adds to the transition at <paramref name="edge"/>.
    /// The host commits the change as one history entry; this default, kept for replacements written before
    /// the member existed, only writes the duration.
    /// </summary>
    void SetTransitionDuration(Element element, ElementEdge edge, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (ElementTransitionEdits.GetTransition(element, edge) is { } transition)
        {
            transition.Duration.CurrentValue = duration;
        }
    }
}
