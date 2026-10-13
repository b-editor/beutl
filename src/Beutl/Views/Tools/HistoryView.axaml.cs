using System.Collections.Specialized;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor;
using Beutl.ViewModels.Tools;

namespace Beutl.Views.Tools;

public partial class HistoryView : UserControl
{
    private CompositeDisposable? _viewModelSubscriptions;
    private HistoryViewModel? _currentViewModel;
    // Guards re-entrant JumpTo calls when SelectedIndex is updated programmatically
    // in response to ViewModel-driven state changes.
    private bool _suppressSelectionChanged;

    public HistoryView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        HistoryList.ContainerPrepared += OnContainerPrepared;
        HistoryList.ContainerClearing += OnContainerClearing;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Detach();
        TryAttach();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Re-attach when the same control is reinserted into the visual tree
        // (e.g. switching dock tabs) where DataContextChanged would not fire
        // again because the DataContext reference did not change.
        TryAttach();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Detach();
        base.OnDetachedFromVisualTree(e);
    }

    private void TryAttach()
    {
        if (_currentViewModel is not null) return;
        if (DataContext is not HistoryViewModel viewModel) return;

        _currentViewModel = viewModel;
        _viewModelSubscriptions = new CompositeDisposable
        {
            viewModel.CurrentIndex.Subscribe(_ => SyncSelection()),
        };

        ((INotifyCollectionChanged)viewModel.Entries).CollectionChanged += OnEntriesChanged;
        _viewModelSubscriptions.Add(Disposable.Create(viewModel,
            vm => ((INotifyCollectionChanged)vm.Entries).CollectionChanged -= OnEntriesChanged));

        SyncSelection();
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
        if (_currentViewModel is null) return;

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
        if (_currentViewModel is null) return;

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

    private async void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged) return;
        if (_currentViewModel is null) return;

        HistoryViewModel viewModel = _currentViewModel;
        int index = HistoryList.SelectedIndex;
        if (index < 0) return;

        await viewModel.JumpTo(index);
        if (_currentViewModel != viewModel) return;

        // Re-sync when JumpTo did not move, so the clicked row does not stay selected and a second
        // click on it jumps again.
        if (viewModel.CurrentIndex.Value != index)
        {
            SyncSelection();
        }
        else
        {
            UpdateItemAppearance();
        }
    }

    private async void OnUndoClick(object? sender, RoutedEventArgs e)
    {
        if (_currentViewModel is { } viewModel)
        {
            await viewModel.Undo();
        }
    }

    private async void OnRedoClick(object? sender, RoutedEventArgs e)
    {
        if (_currentViewModel is { } viewModel)
        {
            await viewModel.Redo();
        }
    }
}
