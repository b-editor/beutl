using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Moves one or more <see cref="Element"/>s on a <see cref="Scene"/>. The
/// service owns the history commit; the View calls <see cref="Move"/> or
/// <see cref="DuplicateOrMove"/> once on drag release and inspects the
/// returned <see cref="ElementMoveOutcome"/> to drive notifications and
/// visual rollback.
/// </summary>
public interface IElementMoveService
{
    /// <summary>Moves <paramref name="elements"/> by the given delta and commits one
    /// MoveElement entry. Returns <see cref="ElementMoveOutcome.None"/> when the delta
    /// is zero or no elements were supplied.</summary>
    ElementMoveOutcome Move(
        Scene scene,
        IReadOnlyList<Element> elements,
        TimeSpan deltaStart,
        int deltaZIndex);

    /// <summary>Places duplicates of <paramref name="elements"/> at
    /// (origin + delta). Falls back to a plain move when the duplicate cannot
    /// be staged (e.g., project has no Uri yet). Returns one of
    /// <see cref="ElementMoveOutcome.Duplicated"/>,
    /// <see cref="ElementMoveOutcome.DuplicateOverlapsSource"/>, or
    /// <see cref="ElementMoveOutcome.FellBackToMove"/>.</summary>
    ElementMoveOutcome DuplicateOrMove(
        Scene scene,
        IReadOnlyList<Element> elements,
        TimeSpan deltaStart,
        int deltaZIndex);
}

public enum ElementMoveOutcome
{
    None,
    Moved,
    Duplicated,
    FellBackToMove,
    DuplicateOverlapsSource,
}
