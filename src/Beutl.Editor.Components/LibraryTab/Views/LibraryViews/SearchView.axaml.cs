using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Beutl.Editor.Components.LibraryTab.ViewModels;

namespace Beutl.Editor.Components.LibraryTab.Views.LibraryViews;

public partial class SearchView : UserControl
{
    private CancellationTokenSource? _cts;

    internal Func<Task> QueryDelay { get; set; } = static () => Task.Delay(100);

    public SearchView()
    {
        InitializeComponent();
        SearchBox.GetObservable(TextBox.TextProperty).Subscribe(SearchQueryChanged);

        searchResult.ContainerPrepared += OnItemContainerPrepared;
        searchResult.ContainerClearing += OnItemContainerClearing;
    }

    private async void SearchQueryChanged(string? str)
    {
        if (DataContext is LibraryTabViewModel viewModel)
        {
            _cts?.Cancel();
            var cts = new CancellationTokenSource();
            _cts = cts;
            await QueryDelay();
            // The delays of two quick changes can end in either order, so only the latest change acts.
            if (!ReferenceEquals(_cts, cts))
                return;

            if (string.IsNullOrWhiteSpace(str))
            {
                searchResult.ItemsSource = viewModel.AllItems;
                viewModel.ClearSearch();
            }
            else
            {
                searchResult.ItemsSource = viewModel.SearchResult;
                await viewModel.Search(str, cts.Token);
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
        LibraryItemViewModel? item;
        if (e.GetCurrentPoint(searchResult).Properties.IsLeftButtonPressed)
        {
            item = (e.Source as StyledElement)?.DataContext as LibraryItemViewModel;
            searchResult.SelectedItem = item;
        }
        else
        {
            return;
        }

        if (item != null)
        {
            await LibraryTreeDragHelper.StartDragAsync(e, item);
        }
    }
}
