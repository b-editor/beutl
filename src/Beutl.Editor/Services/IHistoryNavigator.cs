namespace Beutl.Editor.Services;

// Jumps through the editor's history with the same guard as undo and redo, so a tool tab needs no editor type.
internal interface IHistoryNavigator
{
    ValueTask<bool> JumpToAsync(int index);

    ValueTask<bool> JumpToAsync(HistoryEntry entry, CancellationToken cancellationToken);
}
