namespace Beutl.Extensibility;

/// <summary>
/// An optional capability implemented by editor contexts that support undo and redo.
/// </summary>
public interface IUndoRedoEditorContext : IEditorContext
{
    /// <summary>Undoes the last edit. Called by the host on the UI thread.</summary>
    /// <returns><see langword="true"/> when an edit was undone; otherwise <see langword="false"/>.</returns>
    ValueTask<bool> UndoAsync();

    /// <summary>Redoes the last undone edit. Called by the host on the UI thread.</summary>
    /// <returns><see langword="true"/> when an edit was redone; otherwise <see langword="false"/>.</returns>
    ValueTask<bool> RedoAsync();
}
