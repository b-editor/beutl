using Beutl.Graphics.Transitions;
using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public sealed class ElementAttributeService : IElementAttributeService
{
    private readonly HistoryManager _historyManager;

    public ElementAttributeService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public void SetEnabled(Element element, bool isEnabled)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.IsEnabled == isEnabled) return;

        element.IsEnabled = isEnabled;
        _historyManager.Commit(CommandNames.ChangeElementEnabled);
    }

    public void SetAccentColor(Element element, Color color)
    {
        ArgumentNullException.ThrowIfNull(element);
        // Backstop for a locked clip whose color edit slips past a UI guard (e.g. a color picker
        // confirmed after the clip was locked). Layer-lock is enforced by the caller's IsEditable,
        // which has the scene the element alone lacks.
        if (element.IsLocked) return;
        if (element.AccentColor == color) return;

        element.AccentColor = color;
        _historyManager.Commit(CommandNames.ChangeElementColor);
    }

    public void SetLocked(Element element, bool isLocked)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.IsLocked == isLocked) return;

        element.IsLocked = isLocked;
        _historyManager.Commit(CommandNames.ChangeElementLocked);
    }

    public void SetName(Element element, string name)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(name);
        // Same backstop as SetAccentColor; the caller enforces the layer lock.
        if (element.IsLocked) return;
        if (element.Name == name) return;

        element.Name = name;
        _historyManager.Commit(CommandNames.RenameElement);
    }

    public void ApplyTransition(Element element, ElementEdge edge, Type transitionType)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!ElementTransitionEdits.IsTransitionType(transitionType))
        {
            throw new ArgumentException($"'{transitionType}' is not a transition type that can be created.", nameof(transitionType));
        }

        // Same backstop as SetAccentColor; the caller enforces the layer lock.
        if (element.IsLocked) return;

        Element? partner = ElementTransitionEdits.FindEditablePartner(element, edge);
        ClipTransition? own = ElementTransitionEdits.GetTransition(element, edge);
        ClipTransition? across = partner == null ? null : ElementTransitionEdits.GetTransition(partner, Opposite(edge));
        if (own != null || across != null)
        {
            // An existing boundary keeps its timing and changes how it blends on both sides at once, so
            // the side that decides never disagrees with the other.
            bool changed = false;
            if (own != null && own.GetType() != transitionType)
            {
                changed |= ElementTransitionEdits.SetTransition(
                    element, edge, ElementTransitionEdits.ChangeType(own, transitionType));
            }

            if (across != null && across.GetType() != transitionType)
            {
                changed |= ElementTransitionEdits.SetTransition(
                    partner!, Opposite(edge), ElementTransitionEdits.ChangeType(across, transitionType));
            }

            if (changed)
            {
                _historyManager.Commit(CommandNames.ChangeTransition);
            }

            return;
        }

        // A new boundary is centred on the cut, half on each element; a lone edge takes it all.
        if (partner != null)
        {
            TimeSpan half = ClipTransition.DefaultDuration / 2;
            ElementTransitionEdits.SetTransition(
                element, edge, ElementTransitionEdits.CreateTransition(transitionType, half));
            ElementTransitionEdits.SetTransition(
                partner, Opposite(edge), ElementTransitionEdits.CreateTransition(transitionType, half));
        }
        else
        {
            ElementTransitionEdits.SetTransition(
                element, edge, ElementTransitionEdits.CreateTransition(transitionType, ClipTransition.DefaultDuration));
        }

        _historyManager.Commit(CommandNames.AddTransition);
    }

    public void RemoveTransition(Element element, ElementEdge edge)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.IsLocked) return;

        bool changed = ElementTransitionEdits.SetTransition(element, edge, null);
        if (ElementTransitionEdits.FindEditablePartner(element, edge) is { } partner)
        {
            changed |= ElementTransitionEdits.SetTransition(partner, Opposite(edge), null);
        }

        if (changed)
        {
            _historyManager.Commit(CommandNames.RemoveTransition);
        }
    }

    public void SetTransitionDuration(Element element, ElementEdge edge, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.IsLocked) return;
        if (ElementTransitionEdits.GetTransition(element, edge) is not { } transition) return;
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        if (transition.Duration.CurrentValue == duration) return;

        transition.Duration.CurrentValue = duration;
        _historyManager.Commit(CommandNames.ChangeTransitionDuration);
    }

    private static ElementEdge Opposite(ElementEdge edge) => edge == ElementEdge.Start ? ElementEdge.End : ElementEdge.Start;
}
