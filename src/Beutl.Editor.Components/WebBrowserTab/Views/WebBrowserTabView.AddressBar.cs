using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Beutl.Editor.Components.WebBrowserTab.Views;

internal partial class WebBrowserTabView
{
    private void OnAddressKeyDown(object? sender, KeyEventArgs e)
    {
        if (SearchSuggestionsPopup.IsOpen)
        {
            int count = SearchSuggestionsList.ItemCount;
            if (e.Key is Key.Down or Key.Up && count > 0)
            {
                int index = SearchSuggestionsList.SelectedIndex;
                SearchSuggestionsList.SelectedIndex = e.Key == Key.Down
                    ? (index + 1) % count
                    : (index <= 0 ? count - 1 : index - 1);
                SearchSuggestionsList.ScrollIntoView(SearchSuggestionsList.SelectedItem!);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter && SearchSuggestionsList.SelectedItem is string query)
            {
                SearchForSuggestion(query);
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Escape)
        {
            AddressTextBox.CancelSearchSuggestions();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            AddressTextBox.CancelSearchSuggestions();
            NavigateFromAddress();
            e.Handled = true;
        }
    }

    private void OnSearchSuggestionsChanged(IReadOnlyList<string> suggestions)
    {
        SearchSuggestionsList.ItemsSource = suggestions;
        SearchSuggestionsList.SelectedIndex = -1;
        SearchSuggestionsPopup.IsOpen = !_disposed && suggestions.Count > 0 && AddressTextBox.IsFocused
            && TopLevel.GetTopLevel(this) != null;
    }

    private void OnSearchSuggestionsPopupClosed(object? sender, EventArgs e) => AddressTextBox.CancelSearchSuggestions();

    private void OnSearchSuggestionPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && e.Source is Avalonia.Visual visual
            && visual.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.DataContext is string query)
        {
            SearchForSuggestion(query);
            e.Handled = true;
        }
    }

    private void SearchForSuggestion(string query)
    {
        AddressTextBox.CancelSearchSuggestions();
        if (_viewModel != null)
        {
            _viewModel.Address.Value = WebSearchSuggestions.CreateSearchUri(query, _viewModel.Profile.Engine).AbsoluteUri;
            NavigateFromAddress();
        }
    }
}
