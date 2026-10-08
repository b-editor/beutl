using Beutl.Editor;
using Beutl.ViewModels;

namespace Beutl.Services.PrimitiveImpls;

// Jumps to a history state from the palette, without opening the history tab.
public sealed partial class HistoryTabExtension : IContextCommandHandler
{
    public override IEnumerable<ContextCommandDefinition> ContextCommands =>
    [
        new("JumpToHistory", Strings.JumpToHistory, "", []) { Scope = ContextCommandScope.Extension },
    ];

    public bool CanExecute(ContextCommandExecution execution)
    {
        // The initial entry alone leaves nowhere to go.
        return execution.CommandName == "JumpToHistory"
            && execution.EditorContext is EditViewModel editViewModel
            && editViewModel.HistoryManager.GetEntriesSnapshot().Length > 1;
    }

    public async Task ExecuteAsync(ContextCommandExecution execution)
    {
        if (!CanExecute(execution)
            || execution.Interaction is not { } interaction
            || execution.EditorContext is not EditViewModel editViewModel)
        {
            return;
        }

        HistoryManager history = editViewModel.HistoryManager;
        HistoryEntry[] entries = history.GetEntriesSnapshot();
        int current = history.CurrentIndex;
        // Newest first, the order in which the states are usually wanted back.
        ContextCommandPickItem<HistoryEntry>[] items = entries
            .Select((entry, index) =>
            {
                string time = entry.Timestamp.ToString("T", CultureInfo.CurrentCulture);
                return new ContextCommandPickItem<HistoryEntry>(
                    entry.DisplayLabel, entry, index == current ? $"{time} · {Strings.Current}" : time);
            })
            .Reverse()
            .ToArray();
        if (await interaction.ShowQuickPickAsync(items) is not { } picked) return;

        // A commit while the list was open shifts or drops entries, so the index is looked up again.
        int target = Array.IndexOf(history.GetEntriesSnapshot(), picked.Value);
        if (target >= 0)
        {
            await editViewModel.JumpToHistoryAsync(target);
        }
    }
}
