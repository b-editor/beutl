using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Beutl.ViewModels;

namespace Beutl.Views.LibraryViews;

public partial class SearchView : UserControl
{
    private CancellationTokenSource? _cts;

    public SearchView()
    {
        InitializeComponent();
        SearchBox.GetObservable(TextBox.TextProperty).Subscribe(SearchQueryChanged);

        searchResult.ContainerPrepared += OnItemContainerPrepared;
        searchResult.ContainerClearing += OnItemContainerClearing;
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

    private void OnItemContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is ListBoxItem listItem)
        {
            listItem.AddHandler(PointerPressedEvent, ListBoxItemPointerPressed, RoutingStrategies.Tunnel);
        }
    }

    private void OnItemContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        if (e.Container is ListBoxItem listItem)
        {
            listItem.RemoveHandler(PointerPressedEvent, ListBoxItemPointerPressed);
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
}
