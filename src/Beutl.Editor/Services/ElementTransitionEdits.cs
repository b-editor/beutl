using Beutl.Graphics.Transitions;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.Editor.Services;

/// <summary>Which end of an element a clip boundary transition sits at.</summary>
public enum ElementEdge
{
    Start,

    End,
}

/// <summary>Reads and writes the transition sides of an element without committing history.</summary>
public static class ElementTransitionEdits
{
    public static ClipTransition? GetTransition(Element element, ElementEdge edge)
    {
        ArgumentNullException.ThrowIfNull(element);
        return edge == ElementEdge.Start ? element.EnterTransition : element.ExitTransition;
    }

    // Returns whether the side changed.
    public static bool SetTransition(Element element, ElementEdge edge, ClipTransition? transition)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (ReferenceEquals(GetTransition(element, edge), transition)) return false;

        if (edge == ElementEdge.Start)
            element.EnterTransition = transition;
        else
            element.ExitTransition = transition;
        return true;
    }

    /// <summary>
    /// Gets whether <paramref name="type"/> is a transition that can be created: a concrete
    /// <see cref="ClipTransition"/> with a public parameterless constructor, other than the stand-in for one
    /// whose type could not be loaded.
    /// </summary>
    public static bool IsTransitionType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.IsAssignableTo(typeof(ClipTransition))
               && !type.IsAbstract
               && !type.ContainsGenericParameters
               && !type.IsAssignableTo(typeof(IFallback))
               && type.GetConstructor(Type.EmptyTypes) != null;
    }

    /// <summary>Creates a transition of <paramref name="transitionType"/> lasting <paramref name="duration"/>.</summary>
    public static ClipTransition CreateTransition(Type transitionType, TimeSpan duration)
    {
        if (!IsTransitionType(transitionType))
        {
            throw new ArgumentException($"'{transitionType}' is not a transition type that can be created.", nameof(transitionType));
        }

        var transition = (ClipTransition)Activator.CreateInstance(transitionType)!;
        transition.Duration.CurrentValue = duration;
        return transition;
    }

    /// <summary>
    /// Creates a transition of <paramref name="transitionType"/> with the timing of <paramref name="source"/>,
    /// so a boundary can change how it blends without moving.
    /// </summary>
    public static ClipTransition ChangeType(ClipTransition source, Type transitionType)
    {
        ArgumentNullException.ThrowIfNull(source);
        ClipTransition transition = CreateTransition(transitionType, source.Duration.CurrentValue);
        transition.Easing.CurrentValue = source.Easing.CurrentValue;
        transition.Easing.Expression = source.Easing.Expression;
        transition.IsEnabled = source.IsEnabled;
        return transition;
    }

    /// <summary>
    /// Gets the element that meets <paramref name="edge"/> of <paramref name="element"/> on the same layer,
    /// or <see langword="null"/> when none does.
    /// </summary>
    public static Element? FindPartner(Element element, ElementEdge edge)
    {
        ArgumentNullException.ThrowIfNull(element);
        return edge == ElementEdge.Start
            ? ElementTransitions.FindPrevious(element)
            : ElementTransitions.FindNext(element);
    }

    internal static Element? FindEditablePartner(Element element, ElementEdge edge)
    {
        Element? partner = FindPartner(element, edge);
        if (partner == null || partner.HierarchicalParent is not Scene scene) return partner;

        return scene.IsElementLocked(partner) ? null : partner;
    }
}
