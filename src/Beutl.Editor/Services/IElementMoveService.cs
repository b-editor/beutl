using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Moves one or more <see cref="Element"/>s on a <see cref="Scene"/>. The
/// <see cref="IElementMoveDragSession"/> owns the history commit so the View
/// behavior never calls <c>history.Commit</c> directly. When the user holds
/// Alt during the drag, the session can place duplicates instead of moving.
/// </summary>
public interface IElementMoveService
{
    IElementMoveDragSession BeginMove(
        Scene scene,
        IReadOnlyList<Element> elements,
        Element anchor,
        bool duplicateMode);
}

public interface IElementMoveDragSession : IDisposable
{
    ElementMoveOutcome Commit(TimeSpan deltaStart, int deltaZIndex);

    void Cancel();
}

public enum ElementMoveOutcome
{
    None,
    Moved,
    Duplicated,
    FellBackToMove,
    DuplicateOverlapsSource,
}
