using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Editor.Components.AgentTab.ViewModels;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed partial class AgentModelPickerView : UserControl
{
    private AgentTabViewModel? _viewModel;
    private bool _attached;
    private bool _syncingSelection;

    public AgentModelPickerView()
    {
        InitializeComponent();
        ModelSearchBox.AddHandler(KeyDownEvent, OnSearchBoxKeyDown);
    }

    internal void FocusSearch() => ModelSearchBox.Focus();

    internal void Resize(double composerWidth, double availableHeight)
    {
        Width = Math.Clamp(composerWidth - 16, 220, 500);
        PickerBody.Height = Math.Clamp(availableHeight - 56, 160, 306);
        bool compact = Width < 342;
        PickerBody.ColumnDefinitions[2].Width = new GridLength(compact ? 0 : Width < 435 ? 128 : 156);
        PickerBody.RowDefinitions[1].Height = new GridLength(compact ? PickerBody.Height / 2 : 0);
        Grid.SetRowSpan(ProviderRail, compact ? 2 : 1);
        Grid.SetColumn(EffortPane, compact ? 1 : 2);
        Grid.SetRow(EffortPane, compact ? 1 : 0);
        EffortPane.BorderThickness = compact ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_attached)
            AttachViewModel(DataContext as AgentTabViewModel);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        AttachViewModel(DataContext as AgentTabViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        AttachViewModel(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void AttachViewModel(AgentTabViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
            return;
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = viewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SyncSelections();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AgentTabViewModel.ModelRows) or nameof(AgentTabViewModel.SelectedModelRow)
            or nameof(AgentTabViewModel.ModelEfforts) or nameof(AgentTabViewModel.SelectedEffortRow)
            or null or "")
        {
            SyncSelections();
        }
    }

    private void SyncSelections()
    {
        _syncingSelection = true;
        try
        {
            ModelRows.SelectedItem = _viewModel?.SelectedModelRow;
            EffortRows.SelectedItem = _viewModel?.SelectedEffortRow;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private async void OnBrowseProvider(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { DataContext: AgentModelProviderRow row } toggle && DataContext is AgentTabViewModel viewModel)
        {
            toggle.IsChecked = true;
            FocusSearch();
            await viewModel.BrowseModelsAsync(row.Provider);
        }
    }

    private async void OnModelSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || DataContext is not AgentTabViewModel viewModel)
            return;

        if (ModelRows.SelectedItem is AgentModelRow row)
        {
            if (row.CanSelect && !row.IsSelected)
            {
                await viewModel.ChooseModelAsync(row);
                SyncSelections();
            }
            else if (!row.CanSelect)
            {
                SyncSelections();
            }
        }
    }

    private async void OnEffortSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || DataContext is not AgentTabViewModel viewModel)
            return;

        if (EffortRows.SelectedItem is AgentEffortRow row && !row.IsSelected)
        {
            await viewModel.ChooseEffortAsync(row);
            SyncSelections();
        }
    }

    private void OnShowMoreModels(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AgentTabViewModel viewModel)
        {
            FocusSearch();
            viewModel.ExpandModelList();
        }
    }

    private async void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not AgentTabViewModel viewModel || e.KeyModifiers != KeyModifiers.None || AgentTabView.IsComposing(e))
            return;

        if (e.Key is Key.Up or Key.Down && ModelRows.ItemCount > 0)
        {
            int delta = e.Key == Key.Down ? 1 : -1;
            int index = ModelRows.SelectedIndex;
            int next = index < 0
                ? (delta > 0 ? 0 : ModelRows.ItemCount - 1)
                : Math.Clamp(index + delta, 0, ModelRows.ItemCount - 1);
            if (next != index)
            {
                ModelRows.SelectedIndex = next;
                ModelRows.ScrollIntoView(next);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && viewModel.ModelRows.FirstOrDefault(row => row.CanSelect) is { } first)
        {
            var target = ModelRows.SelectedItem is AgentModelRow { CanSelect: true } selected ? selected : first;
            e.Handled = true;
            await viewModel.ChooseModelAsync(target);
            SyncSelections();
        }
    }
}
