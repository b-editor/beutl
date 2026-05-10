using System.Collections.Specialized;
using System.Reactive.Disposables;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor;
using Beutl.Logging;
using Beutl.ViewModels.Tools;
using Microsoft.Extensions.Logging;

namespace Beutl.Views.Tools;

public partial class HistoryView : UserControl
{
    private readonly ILogger _logger = Log.CreateLogger<HistoryView>();
    private CompositeDisposable? _viewModelSubscriptions;
    private HistoryViewModel? _currentViewModel;
    // Guards re-entrant JumpTo calls when SelectedIndex is updated programmatically
    // in response to ViewModel-driven state changes.
    private bool _suppressSelectionChanged;

    public HistoryView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        HistoryList.ContainerPrepared += OnContainerPrepared;
        HistoryList.ContainerClearing += OnContainerClearing;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Detach();

        if (DataContext is HistoryViewModel viewModel)
        {
            _currentViewModel = viewModel;
            _viewModelSubscriptions = new CompositeDisposable
            {
                viewModel.CurrentIndex.Subscribe(
                    _ => SyncSelection(),
                    ex => _logger.LogError(ex, "CurrentIndex stream errored; selection will stop syncing")),
            };

            ((INotifyCollectionChanged)viewModel.Entries).CollectionChanged += OnEntriesChanged;
            _viewModelSubscriptions.Add(Disposable.Create(viewModel,
                vm => ((INotifyCollectionChanged)vm.Entries).CollectionChanged -= OnEntriesChanged));

            SyncSelection();
        }
    }

    private void OnDetachedFromVisualTree(object? sender, EventArgs e)
    {
        Detach();
    }

    private void Detach()
    {
        _viewModelSubscriptions?.Dispose();
        _viewModelSubscriptions = null;
        _currentViewModel = null;
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncSelection();
    }

    private void SyncSelection()
    {
        if (_currentViewModel is null || HistoryList is null) return;

        int target = _currentViewModel.CurrentIndex.Value;
        if (target < 0 || target >= _currentViewModel.Entries.Count)
        {
            return;
        }

        try
        {
            _suppressSelectionChanged = true;
            HistoryList.SelectedIndex = target;
        }
        finally
        {
            _suppressSelectionChanged = false;
        }

        UpdateItemAppearance();
    }

    private void UpdateItemAppearance()
    {
        if (_currentViewModel is null || HistoryList is null) return;

        int current = _currentViewModel.CurrentIndex.Value;
        for (int i = 0; i < _currentViewModel.Entries.Count; i++)
        {
            if (HistoryList.ContainerFromIndex(i) is not ListBoxItem item) continue;

            ApplyItemClasses(item, i, current);
        }
    }

    // ContainerPrepared fires whenever a ListBoxItem becomes realized
    // (including after recycling), so this is where freshly attached
    // containers get their current/future classes set.
    private void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (_currentViewModel is null || e.Container is not ListBoxItem item) return;

        ApplyItemClasses(item, e.Index, _currentViewModel.CurrentIndex.Value);
    }

    // Strip our state classes when a container is recycled so it does not
    // re-appear at a new index carrying stale styling.
    private void OnContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        if (e.Container is not ListBoxItem item) return;

        item.Classes.Set("current", false);
        item.Classes.Set("future", false);
    }

    private static void ApplyItemClasses(ListBoxItem item, int index, int currentIndex)
    {
        item.Classes.Set("current", index == currentIndex);
        item.Classes.Set("future", index > currentIndex);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged) return;
        if (_currentViewModel is null || HistoryList is null) return;

        int index = HistoryList.SelectedIndex;
        if (index < 0) return;

        _currentViewModel.JumpTo(index);
        UpdateItemAppearance();
    }

    private void OnUndoClick(object? sender, RoutedEventArgs e)
    {
        _currentViewModel?.Undo();
    }

    private void OnRedoClick(object? sender, RoutedEventArgs e)
    {
        _currentViewModel?.Redo();
    }
}
