using Beutl.Editor;
using Beutl.ViewModels;

namespace Beutl.Services.PrimitiveImpls;

// Jumps to a history state from the palette, without opening the history tab.
public sealed partial class HistoryTabExtension
{
    public override IEnumerable<ContextCommandDefinition> ContextCommands =>
    [
        new("history.jumpToHistory", Strings.JumpToHistory, "", [])
        {
            Scope = ContextCommandScope.Extension,
            CanExecute = CanJumpToHistory,
            ExecuteAsync = JumpToHistoryAsync
        },
    ];

    private static bool CanJumpToHistory(ContextCommandExecution execution)
    {
        // The initial entry alone leaves nowhere to go, unless an uncommitted edit (a nudge still in its
        // debounce) can be reverted to it.
        return execution.EditorContext is EditViewModel { HistoryManager: var history }
            && (history.GetEntriesSnapshot().Length > 1 || history.HasPendingOperations);
    }

    private static async Task JumpToHistoryAsync(ContextCommandExecution execution)
    {
        if (!CanJumpToHistory(execution)
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
        if (await interaction.ShowQuickPickAsync(items) is { } picked)
        {
            await editViewModel.JumpToHistoryAsync(picked.Value, interaction.CancellationToken);
        }
    }
}
