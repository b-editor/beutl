using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Moves one or more <see cref="Element"/>s on a <see cref="Scene"/>, owning the
/// history commit. The View calls <see cref="Move"/> / <see cref="DuplicateOrMove"/>
/// once on drag release and inspects the <see cref="ElementMoveOutcome"/> to drive
/// notifications and visual rollback.
/// </summary>
public interface IElementMoveService
{
    /// <summary>Moves <paramref name="elements"/> by the given delta and commits one
    /// MoveElement entry. <see cref="ElementMoveOutcome.None"/> means nothing was
    /// moved or committed.</summary>
    ElementMoveOutcome Move(
        Scene scene,
        IReadOnlyList<Element> elements,
        MediaTime deltaStart,
        int deltaZIndex);

    /// <summary>Places duplicates of <paramref name="elements"/> at (origin + delta),
    /// falling back to a plain move when the duplicate cannot be staged (e.g. project
    /// has no Uri yet). Returns <see cref="ElementMoveOutcome.Duplicated"/>,
    /// <see cref="ElementMoveOutcome.DuplicateOverlapsSource"/>,
    /// <see cref="ElementMoveOutcome.FellBackToMove"/>, or <see cref="ElementMoveOutcome.None"/>
    /// when nothing was done.</summary>
    ElementMoveOutcome DuplicateOrMove(
        Scene scene,
        IReadOnlyList<Element> elements,
        MediaTime deltaStart,
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
