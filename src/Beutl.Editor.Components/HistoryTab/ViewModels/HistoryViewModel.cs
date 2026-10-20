using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive.Disposables;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Beutl.Editor.Services;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Editor.Components.HistoryTab.ViewModels;

public sealed class HistoryViewModel : IToolContext
{
    private readonly ILogger _logger = Log.CreateLogger<HistoryViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly IEditorContext _editorContext;
    private readonly HistoryManager _historyManager;
    private readonly IHistoryNavigator _navigator;
    private readonly IUndoRedoEditorContext _undoRedo;
    private readonly ObservableCollection<HistoryEntry> _entries = [];
    private readonly ReactivePropertySlim<int> _currentIndex;

    public HistoryViewModel(IEditorContext editorContext)
    {
        _editorContext = editorContext;
        _historyManager = editorContext.GetRequiredService<HistoryManager>();
        _navigator = editorContext.GetRequiredService<IHistoryNavigator>();
        _undoRedo = editorContext.GetRequiredService<IUndoRedoEditorContext>();

        Entries = new ReadOnlyObservableCollection<HistoryEntry>(_entries);

        // SubscribeEntries takes the snapshot, current index and subscription under one lock, so no commit slips in between.
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

        // Re-sync once to cover a change made between SubscribeEntries and the StateChanged subscription.
        DispatchToUI(SyncCurrentIndex);
    }

    public ToolTabExtension Extension => HistoryTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>(Strings.History);

    public ReadOnlyObservableCollection<HistoryEntry> Entries { get; }

    public IReadOnlyReactiveProperty<int> CurrentIndex { get; }

    public async Task JumpTo(int index)
    {
        await _navigator.JumpToAsync(index);
    }

    public async Task Undo()
    {
        await _undoRedo.UndoAsync();
    }

    public async Task Redo()
    {
        await _undoRedo.RedoAsync();
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }

    public object? GetService(Type serviceType)
    {
        return _editorContext.GetService(serviceType);
    }

    public void ReadFromJson(JsonObject json)
    {
        // History is editor-session state and is not persisted across loads.
    }

    public void WriteToJson(JsonObject json)
    {
    }

    private void OnManagerEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DispatchToUI(() => ApplyManagerChange(e));
    }

    // Mirrors each change granularly so the ListBox keeps selection and scroll. HistoryManager sends
    // only Add, Replace and Remove, each with one entry at a valid index.
    private void ApplyManagerChange(NotifyCollectionChangedEventArgs e)
    {
        try
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    ApplyAdd(e.NewStartingIndex, e.NewItems!);
                    break;
                case NotifyCollectionChangedAction.Remove:
                    ApplyRemove(e.OldStartingIndex, e.OldItems!);
                    break;
                case NotifyCollectionChangedAction.Replace:
                    ApplyReplace(e.NewStartingIndex, e.NewItems!);
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

    private void ApplyAdd(int insertIndex, IList newItems)
    {
        foreach (HistoryEntry entry in newItems)
        {
            _entries.Insert(insertIndex++, entry);
        }
    }

    private void ApplyRemove(int removeAt, IList oldItems)
    {
        for (int i = 0; i < oldItems.Count; i++)
        {
            if (removeAt < _entries.Count)
            {
                _entries.RemoveAt(removeAt);
            }
        }
    }

    private void ApplyReplace(int start, IList newItems)
    {
        for (int i = 0; i < newItems.Count; i++)
        {
            if (start + i < _entries.Count)
            {
                _entries[start + i] = (HistoryEntry)newItems[i]!;
            }
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
