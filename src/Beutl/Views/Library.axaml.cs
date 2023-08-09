using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Generators;
using Avalonia.Input;
using Avalonia.Interactivity;

using Beutl.Services;
using Beutl.ViewModels;

namespace Beutl.Views;

public sealed partial class Library : UserControl
{
    private CancellationTokenSource? _cts;

    public Library()
    {
        InitializeComponent();
        SearchBox.GetObservable(TextBox.TextProperty).Subscribe(SearchQueryChanged);

        NodeTreeView.ContainerPrepared += OnItemContainerPrepared;
        NodeTreeView.ContainerClearing += OnItemContainerClearing;
        LibraryTree.ContainerPrepared += OnItemContainerPrepared;
        LibraryTree.ContainerClearing += OnItemContainerClearing;

        searchResult.ContainerPrepared += OnItemContainerPrepared;
        searchResult.ContainerClearing += OnItemContainerClearing;

        itemsControl.AddHandler(PointerPressedEvent, OnEasingsPointerPressed, RoutingStrategies.Tunnel);
        splineEasing.AddHandler(PointerPressedEvent, OnSplineEasingPointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnItemContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is TreeViewItem treeItem)
        {
            treeItem.AddHandler(PointerPressedEvent, TreeViewPointerPressed, RoutingStrategies.Tunnel);
        }
        else if (e.Container is ListBoxItem listItem)
        {
            listItem.AddHandler(PointerPressedEvent, ListBoxItemPointerPressed, RoutingStrategies.Tunnel);
        }
    }

    private void OnItemContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        if (e.Container is TreeViewItem treeItem)
        {
            treeItem.RemoveHandler(PointerPressedEvent, TreeViewPointerPressed);
        }
        else if (e.Container is ListBoxItem listItem)
        {
            listItem.RemoveHandler(PointerPressedEvent, ListBoxItemPointerPressed);
        }
    }

    private async void TreeViewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        LibraryItemViewModel? item;
        if (e.GetCurrentPoint(LibraryTree).Properties.IsLeftButtonPressed)
        {
            item = (e.Source as StyledElement)?.DataContext as LibraryItemViewModel;
            LibraryTree.SelectedItem = item;
        }
        else if (e.GetCurrentPoint(NodeTreeView).Properties.IsLeftButtonPressed)
        {
            item = (e.Source as StyledElement)?.DataContext as LibraryItemViewModel;
            NodeTreeView.SelectedItem = item;
        }
        else
        {
            return;
        }

        if (item != null)
        {
            (string, Type)[] arr = item.TryDragDrop().ToArray();
            if (arr.Length > 0)
            {
                var dataObject = new DataObject();
                foreach ((string format, Type type) in arr)
                {
                    dataObject.Set(format, type);
                }

                await DragDrop.DoDragDrop(e, dataObject, DragDropEffects.Copy);
            }
        }
    }

    private async void ListBoxItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        KeyValuePair<int, LibraryItemViewModel>? item;
        if (e.GetCurrentPoint(searchResult).Properties.IsLeftButtonPressed)
        {
            item = (e.Source as StyledElement)?.DataContext as KeyValuePair<int, LibraryItemViewModel>?;
            searchResult.SelectedItem = item;
        }
        else
        {
            return;
        }

        if (item.HasValue)
        {
            (string, Type)[] arr = item.Value.Value.TryDragDrop().ToArray();
            if (arr.Length > 0)
            {
                var dataObject = new DataObject();
                foreach ((string format, Type type) in arr)
                {
                    dataObject.Set(format, type);
                }

                await DragDrop.DoDragDrop(e, dataObject, DragDropEffects.Copy);
            }
        }
    }

    private async void OnSplineEasingPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var data = new DataObject();
        data.Set(KnownLibraryItemFormats.Easing, new Animation.Easings.SplineEasing());
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy | DragDropEffects.Link);
    }

    private async void OnEasingsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (itemsControl.ItemsSource is { } items)
        {
            foreach (object? item in items)
            {
                Control? control = itemsControl.ContainerFromItem(item);

                if (control?.IsPointerOver == true)
                {
                    var data = new DataObject();
                    data.Set(KnownLibraryItemFormats.Easing, item);
                    await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy | DragDropEffects.Link);
                    return;
                }
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
                searchResult.ItemsSource = viewModel.AllItems;
                viewModel.SearchResult.Clear();
            }
            else
            {
                _cts = new CancellationTokenSource();
                searchResult.ItemsSource = viewModel.SearchResult;
                await viewModel.Search(str, _cts.Token);
            }
        }
    }
}
