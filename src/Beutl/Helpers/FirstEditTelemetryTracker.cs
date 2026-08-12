using System.Collections.Specialized;

namespace Beutl.Helpers;

internal sealed class FirstEditTelemetryTracker(Action recordFirstEdit)
{
    private readonly Action _recordFirstEdit = recordFirstEdit;
    private int _recorded;

    internal void OnHistoryEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // HistoryManager adds an entry only for a newly committed mutation. Undo,
        // redo, jump, and clear use existing entries and therefore cannot report
        // a first edit by accident.
        if (e.Action == NotifyCollectionChangedAction.Add
            && Interlocked.CompareExchange(ref _recorded, 1, 0) == 0)
        {
            _recordFirstEdit();
        }
    }
}
