using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Generators;
using Avalonia.Input;
using Avalonia.Interactivity;

using Beutl.ViewModels;

namespace Beutl.Views;

public sealed partial class Library : UserControl
{
    private CancellationTokenSource? _cts;

    public Library()
    {
        InitializeComponent();
        SearchBox.GetObservable(TextBox.TextProperty).Subscribe(SearchQueryChanged);

        NodeTreeView.ItemContainerGenerator.Index!.Materialized += OnItemMaterialized;
        NodeTreeView.ItemContainerGenerator.Index!.Dematerialized += OnItemDematerialized;
        OperatorTree.ItemContainerGenerator.Index!.Materialized += OnItemMaterialized;
        OperatorTree.ItemContainerGenerator.Index!.Dematerialized += OnItemDematerialized;

        searchResult.ItemContainerGenerator.Materialized += OnItemMaterialized;
        searchResult.ItemContainerGenerator.Dematerialized += OnItemDematerialized;

        itemsControl.AddHandler(PointerPressedEvent, OnEasingsPointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnItemMaterialized(object? sender, ItemContainerEventArgs e)
    {
        foreach (ItemContainerInfo item in e.Containers)
        {
            if (item.ContainerControl is TreeViewItem treeItem)
            {
                treeItem.AddHandler(PointerPressedEvent, TreeViewPointerPressed, RoutingStrategies.Tunnel);
            }
            else if (item.ContainerControl is ListBoxItem listItem)
            {
                listItem.AddHandler(PointerPressedEvent, ListBoxItemPointerPressed, RoutingStrategies.Tunnel);
            }
        }
    }

    private void OnItemDematerialized(object? sender, ItemContainerEventArgs e)
    {
        foreach (ItemContainerInfo item in e.Containers)
        {
            if (item.ContainerControl is TreeViewItem treeItem)
            {
                treeItem.RemoveHandler(PointerPressedEvent, TreeViewPointerPressed);
            }
            else if (item.ContainerControl is ListBoxItem listItem)
            {
                listItem.RemoveHandler(PointerPressedEvent, ListBoxItemPointerPressed);
            }
        }
    }

    private async void TreeViewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is TreeViewItem select
            && select.DataContext is LibraryItemViewModel item)
        {
            if (e.GetCurrentPoint(OperatorTree).Properties.IsLeftButtonPressed)
            {
                OperatorTree.SelectedItem = select;
                await Task.Delay(10);
            }
            else if (e.GetCurrentPoint(NodeTreeView).Properties.IsLeftButtonPressed)
            {
                NodeTreeView.SelectedItem = select;
                await Task.Delay(10);
            }
            else
            {
                return;
            }

            if (item.TryDragDrop(out string? format, out object? data))
            {
                var dataObject = new DataObject();
                dataObject.Set(format, data);
                await DragDrop.DoDragDrop(e, dataObject, DragDropEffects.Copy);
            }
        }
    }

    private async void ListBoxItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is ListBoxItem select
            && select.DataContext is KeyValuePair<int, LibraryItemViewModel> item)
        {
            if (e.GetCurrentPoint(searchResult).Properties.IsLeftButtonPressed)
            {
                searchResult.SelectedItem = select;
                await Task.Delay(10);
            }
            else
            {
                return;
            }

            if (item.Value.TryDragDrop(out string? format, out object? data))
            {
                var dataObject = new DataObject();
                dataObject.Set(format, data);
                await DragDrop.DoDragDrop(e, dataObject, DragDropEffects.Copy);
            }
        }
    }

    private async void OnEasingsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (itemsControl.Items is { } items)
        {
            int index = 0;
            foreach (object? item in items)
            {
                IControl? control = itemsControl.ItemContainerGenerator.ContainerFromIndex(index);

                if (control?.IsPointerOver == true)
                {
                    var data = new DataObject();
                    data.Set("Easing", item);
                    await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy | DragDropEffects.Link);
                    return;
                }

                index++;
            }
        }
    }

    private async void SearchQueryChanged(string? str)
    {
        if (DataContext is LibraryViewModel viewModel)
        {
            _cts?.Cancel();
            await Task.Delay(100);

            if (string.IsNullOrWhiteSpace(str))
            {
                searchResult.Items = viewModel.AllItems;
                viewModel.SearchResult.Clear();
            }
            else
            {
                _cts = new CancellationTokenSource();
                searchResult.Items = viewModel.SearchResult;
                await viewModel.Search(str, _cts.Token);
            }
        }
    }
}
