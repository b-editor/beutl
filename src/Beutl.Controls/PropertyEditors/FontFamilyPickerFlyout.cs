using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Beutl.Language;
using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.Controls.PropertyEditors;

public sealed class FontFamilyPickerFlyout(FontFamilyPickerFlyoutViewModel viewModel) : FAPickerFlyoutBase
{
    public event TypedEventHandler<FontFamilyPickerFlyout, EventArgs>? Confirmed;

    public event TypedEventHandler<FontFamilyPickerFlyout, EventArgs>? Dismissed;

    public event TypedEventHandler<FontFamilyPickerFlyout, PinnableLibraryItem>? Pinned;

    public event TypedEventHandler<FontFamilyPickerFlyout, PinnableLibraryItem>? Unpinned;

    protected override Control CreatePresenter()
    {
        // Fonts are found by name far more often than by scrolling, so the search box starts open.
        var pfp = new LibraryItemPickerFlyoutPresenter
        {
            ShowSearchBox = true,
            NoResultsText = Strings.FontFamilyPicker_NoResults
        };

        pfp.CloseClicked += OnFlyoutDismissed;
        pfp.Confirmed += OnFlyoutConfirmed;
        pfp.Dismissed += OnFlyoutDismissed;
        pfp.Pinned += item => Pinned?.Invoke(this, item);
        pfp.Unpinned += item => Unpinned?.Invoke(this, item);
        pfp.Items = viewModel.Items;
        pfp.SelectedItem = viewModel.SelectedItem.Value;
        pfp.GetObservable(LibraryItemPickerFlyoutPresenter.SelectedItemProperty)
            .Subscribe(v => viewModel.SelectedItem.Value = v);
        // A search highlights its best match, so the selection also flows from the view model.
        viewModel.SelectedItem.Subscribe(v => pfp.SelectedItem = v);
        pfp.GetObservable(LibraryItemPickerFlyoutPresenter.ShowAllProperty)
            .Subscribe(v => viewModel.ShowAll.Value = v);
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
        pfp.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                OnConfirmed();
            }
        };
        pfp.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            switch (e.Key)
            {
                // Escape cancels the picker even from the search box, which would otherwise only close the box.
                case Key.Escape:
                    e.Handled = true;
                    Dismissed?.Invoke(this, EventArgs.Empty);
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

    protected override void OnOpened()
    {
        base.OnOpened();
        if (Popup.Child is LibraryItemPickerFlyoutPresenter pfp)
        {
            pfp.FocusInitialElement();
        }
    }

    private void OnFlyoutDismissed(DraggablePickerFlyoutPresenter sender, object args)
    {
        Dismissed?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    private void OnFlyoutConfirmed(DraggablePickerFlyoutPresenter sender, object args)
    {
        OnConfirmed();
    }

    protected override void OnConfirmed()
    {
        // Enter, the checkmark and any other way of confirming pick from the results for everything typed.
        viewModel.FlushSearch();
        Confirmed?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    protected override void OnOpening(CancelEventArgs args)
    {
        base.OnOpening(args);
        if (Popup.Child is LibraryItemPickerFlyoutPresenter pfp)
        {
            pfp.Theme = Popup.FindResource("FontFamilyPickerFlyoutPresenter") as ControlTheme;
        }

        Popup.IsLightDismissEnabled = false;
    }

    protected override bool ShouldShowConfirmationButtons() => true;
}
