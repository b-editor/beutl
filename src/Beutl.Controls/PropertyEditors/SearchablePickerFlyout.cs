using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;

namespace Beutl.Controls.PropertyEditors;

// Picks an item of an ISearchablePickerViewModel. Enter or the checkmark confirms the highlighted item, and Escape
// cancels even from the search box.
public sealed class SearchablePickerFlyout(ISearchablePickerViewModel viewModel)
    : ConfirmablePickerFlyout<SearchablePickerFlyout, LibraryItemPickerFlyoutPresenter>
{
    // The key of the presenter's ControlTheme, which lays out the items.
    public string PresenterThemeKey { get; init; } = "SearchablePickerFlyoutPresenter";

    // Shown in place of the list when a search matches nothing.
    public string? NoResultsText { get; init; }

    protected override LibraryItemPickerFlyoutPresenter CreatePickerPresenter()
    {
        // These items are found by name far more often than by scrolling, so the search box starts open.
        var pfp = new LibraryItemPickerFlyoutPresenter
        {
            ShowSearchBox = true,
            NoResultsText = NoResultsText
        };

        pfp.Pinned += viewModel.Pin;
        pfp.Unpinned += viewModel.Unpin;
        pfp.Items = viewModel.Items;
        pfp.SelectedItem = viewModel.SelectedItem.Value;
        pfp.GetObservable(LibraryItemPickerFlyoutPresenter.SelectedItemProperty)
            .Subscribe(v => viewModel.SelectedItem.Value = v);
        // A search highlights its best match, so the selection also flows from the view model.
        viewModel.SelectedItem.Subscribe(v => pfp.SelectedItem = v);
        pfp.GetObservable(LibraryItemPickerFlyoutPresenter.SearchTextProperty)
            .Subscribe(v => viewModel.SearchText.Value = v);
        // Hiding the search box drops its query, so the list never stays filtered by text nobody can see.
        pfp.GetObservable(LibraryItemPickerFlyoutPresenter.ShowSearchBoxProperty)
            .Subscribe(v =>
            {
                if (!v)
                {
                    pfp.SearchText = null;
                }
            });
        pfp.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            switch (e.Key)
            {
                // Escape cancels the picker even from the search box, which would otherwise only close the box.
                case Key.Escape:
                    e.Handled = true;
                    Hide();
                    break;
                // These move through the results, so show the results for everything typed so far first.
                case Key.Up or Key.Down:
                    viewModel.FlushSearch();
                    break;
            }
        }, RoutingStrategies.Tunnel);

        return pfp;
    }

    protected override void OnConfirmed()
    {
        // Enter, the checkmark and any other way of confirming pick from the results for everything typed.
        viewModel.FlushSearch();
        base.OnConfirmed();
    }

    protected override void OnOpening(CancelEventArgs args)
    {
        base.OnOpening(args);
        if (Popup.Child is LibraryItemPickerFlyoutPresenter pfp)
        {
            pfp.Theme = Popup.FindResource(PresenterThemeKey) as ControlTheme;
        }
    }
}
