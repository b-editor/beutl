using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor;
using Beutl.ViewModels.Tools;

namespace Beutl.Views.Tools;

public partial class HistoryView : UserControl
{
    private IDisposable? _currentIndexSubscription;
    private HistoryViewModel? _currentViewModel;
    private bool _suppressSelectionChanged;

    public HistoryView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += (_, _) => Detach();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Detach();

        if (DataContext is HistoryViewModel viewModel)
        {
            _currentViewModel = viewModel;
            _currentIndexSubscription = viewModel.CurrentIndex
                .Subscribe(_ => SyncSelection());

            if (viewModel.Entries is INotifyCollectionChanged ncc)
            {
                ncc.CollectionChanged += OnEntriesChanged;
            }

            SyncSelection();
        }
    }

    private void Detach()
    {
        _currentIndexSubscription?.Dispose();
        _currentIndexSubscription = null;
        if (_currentViewModel is { Entries: INotifyCollectionChanged ncc })
        {
            ncc.CollectionChanged -= OnEntriesChanged;
        }
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

            item.Classes.Set("current", i == current);
            item.Classes.Set("future", i > current);
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged) return;
        if (_currentViewModel is null) return;
        if (e.AddedItems is null || e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is not HistoryEntry entry) return;

        _currentViewModel.JumpTo(entry);
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
