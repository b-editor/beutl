using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive.Disposables;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Beutl.Editor;
using Beutl.Logging;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.Tools;

public sealed class HistoryViewModel : IToolContext
{
    private readonly ILogger _logger = Log.CreateLogger<HistoryViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly EditViewModel _editViewModel;
    private readonly HistoryManager _historyManager;
    private readonly ObservableCollection<HistoryEntry> _entries = [];
    private readonly ReactivePropertySlim<int> _currentIndex;

    public HistoryViewModel(EditViewModel editViewModel)
    {
        _editViewModel = editViewModel;
        _historyManager = editViewModel.HistoryManager;

        Entries = new ReadOnlyObservableCollection<HistoryEntry>(_entries);

        // Atomically capture entries snapshot, current index, and subscribe in
        // one lock acquisition. Reading CurrentIndex separately would let a
        // commit slip in between the index read and the subscription, leaving
        // _currentIndex stale until the next history mutation.
        (IDisposable subscription, HistoryEntry[] initialSnapshot, int initialCurrentIndex) =
            _historyManager.SubscribeEntries(OnManagerEntriesChanged);
        _disposables.Add(subscription);

        foreach (HistoryEntry entry in initialSnapshot)
        {
            _entries.Add(entry);
        }

        _currentIndex = new ReactivePropertySlim<int>(initialCurrentIndex);
        CurrentIndex = _currentIndex;

        _historyManager.StateChanged
            .Subscribe(
                _ => DispatchToUI(SyncCurrentIndex),
                ex =>
                {
                    _logger.LogError(ex, "HistoryManager.StateChanged stream errored; UI sync stopped");
                    NotificationService.ShowError(Strings.History, Strings.History_OperationFailed);
                })
            .DisposeWith(_disposables);

        // A commit/clear/jump that fired between SubscribeEntries returning
        // and the StateChanged subscription above would have updated entries
        // (delivered to OnManagerEntriesChanged) but bypassed CurrentIndex.
        // Re-sync once now to close that window.
        DispatchToUI(SyncCurrentIndex);
    }

    public ToolTabExtension Extension => HistoryTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>(Strings.History);

    public ReadOnlyObservableCollection<HistoryEntry> Entries { get; }

    public IReadOnlyReactiveProperty<int> CurrentIndex { get; }

    public async Task JumpTo(int index)
    {
        await _editViewModel.JumpToHistoryAsync(index);
    }

    public async Task Undo()
    {
        await _editViewModel.UndoAsync();
    }

    public async Task Redo()
    {
        await _editViewModel.RedoAsync();
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }

    public object? GetService(Type serviceType)
    {
        return _editViewModel.GetService(serviceType);
    }

    public void ReadFromJson(JsonObject json)
    {
        // History is editor-session state and is not persisted across loads.
    }

    public void WriteToJson(JsonObject json)
    {
        // History is editor-session state and is not persisted across loads.
    }

    private void OnManagerEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DispatchToUI(() => ApplyManagerChange(e));
    }

    // Mirrors a single CollectionChanged event from HistoryManager onto the
    // UI-thread-bound _entries. Granular Add/Remove/Replace/Move events let
    // the ListBox preserve selection, scroll position, and virtualization
    // caches instead of being torn down by a Reset on every commit.
    private void ApplyManagerChange(NotifyCollectionChangedEventArgs e)
    {
        try
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                    ApplyAdd(e.NewStartingIndex, e.NewItems);
                    break;
                case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                    ApplyRemove(e.OldStartingIndex, e.OldItems);
                    break;
                case NotifyCollectionChangedAction.Replace when e.NewItems is not null:
                    ApplyReplace(e.NewStartingIndex, e.NewItems);
                    break;
                case NotifyCollectionChangedAction.Move:
                    ApplyMove(e.OldStartingIndex, e.NewStartingIndex);
                    break;
                case NotifyCollectionChangedAction.Reset:
                default:
                    ResyncEntries();
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply HistoryManager.Entries change ({Action}); falling back to full resync", e.Action);
            ResyncEntries();
        }

        SyncCurrentIndex();
    }

    private void ApplyAdd(int startingIndex, IList newItems)
    {
        int insertIndex = startingIndex >= 0 ? startingIndex : _entries.Count;
        foreach (object? item in newItems)
        {
            if (item is HistoryEntry entry)
            {
                _entries.Insert(insertIndex++, entry);
            }
        }
    }

    private void ApplyRemove(int removeAt, IList oldItems)
    {
        if (removeAt < 0)
        {
            ResyncEntries();
        }
        else
        {
            for (int i = 0; i < oldItems.Count; i++)
            {
                if (removeAt < _entries.Count)
                {
                    _entries.RemoveAt(removeAt);
                }
            }
        }
    }

    private void ApplyReplace(int start, IList newItems)
    {
        if (start < 0)
        {
            ResyncEntries();
            return;
        }
        for (int i = 0; i < newItems.Count; i++)
        {
            if (newItems[i] is HistoryEntry entry && start + i < _entries.Count)
            {
                _entries[start + i] = entry;
            }
        }
    }

    private void ApplyMove(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || newIndex < 0
            || oldIndex >= _entries.Count
            || newIndex >= _entries.Count)
        {
            ResyncEntries();
        }
        else
        {
            _entries.Move(oldIndex, newIndex);
        }
    }

    private void ResyncEntries()
    {
        HistoryEntry[] snapshot;
        try
        {
            snapshot = _historyManager.GetEntriesSnapshot();
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "ResyncEntries skipped — manager is disposed; clearing UI-side mirror");
            _entries.Clear();
            return;
        }

        _entries.Clear();
        foreach (HistoryEntry entry in snapshot)
        {
            _entries.Add(entry);
        }
    }

    private void SyncCurrentIndex()
    {
        try
        {
            _currentIndex.Value = _historyManager.CurrentIndex;
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "SyncCurrentIndex skipped — manager is disposed");
        }
    }

    private void DispatchToUI(Action action)
    {
        void RunGuarded()
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in UI-thread history sync");
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            RunGuarded();
        }
        else
        {
            Dispatcher.UIThread.Post(RunGuarded);
        }
    }
}
