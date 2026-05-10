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
        _currentIndex = new ReactivePropertySlim<int>(_historyManager.CurrentIndex);
        CurrentIndex = _currentIndex;

        SyncEntriesAndIndex();

        if (_historyManager.Entries is INotifyCollectionChanged notifying)
        {
            notifying.CollectionChanged += OnManagerEntriesChanged;
            _disposables.Add(Disposable.Create(() => notifying.CollectionChanged -= OnManagerEntriesChanged));
        }

        _historyManager.StateChanged
            .Subscribe(
                _ => DispatchToUI(SyncCurrentIndex),
                ex => _logger.LogError(ex, "HistoryManager.StateChanged stream errored"))
            .DisposeWith(_disposables);
    }

    public ToolTabExtension Extension => HistoryTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public string Header => Strings.History;

    public ReadOnlyObservableCollection<HistoryEntry> Entries { get; }

    public IReadOnlyReactiveProperty<int> CurrentIndex { get; }

    public void JumpTo(int index)
    {
        try
        {
            _historyManager.JumpTo(index);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException ex)
        {
            ReportFailure(ex, $"Failed to jump to history index {index}");
        }
    }

    public void Undo()
    {
        try
        {
            _historyManager.Undo();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException ex)
        {
            ReportFailure(ex, "Failed to undo");
        }
    }

    public void Redo()
    {
        try
        {
            _historyManager.Redo();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException ex)
        {
            ReportFailure(ex, "Failed to redo");
        }
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
        DispatchToUI(SyncEntriesAndIndex);
    }

    private void SyncEntriesAndIndex()
    {
        SyncEntries();
        SyncCurrentIndex();
    }

    private void SyncEntries()
    {
        var snapshot = _historyManager.Entries.ToList();
        _entries.Clear();
        foreach (HistoryEntry entry in snapshot)
        {
            _entries.Add(entry);
        }
    }

    private void SyncCurrentIndex()
    {
        _currentIndex.Value = _historyManager.CurrentIndex;
    }

    private static void DispatchToUI(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    private void ReportFailure(Exception ex, string context)
    {
        _logger.LogError(ex, "{Context}", context);
        NotificationService.ShowError(Strings.History, Strings.History_OperationFailed);
    }
}
