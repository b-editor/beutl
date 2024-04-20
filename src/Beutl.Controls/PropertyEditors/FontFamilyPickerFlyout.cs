using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.Controls.PropertyEditors;

public sealed class FontFamilyPickerFlyout(FontFamilyPickerFlyoutViewModel viewModel) : PickerFlyoutBase
{
    public event TypedEventHandler<FontFamilyPickerFlyout, EventArgs> Confirmed;

    public event TypedEventHandler<FontFamilyPickerFlyout, EventArgs> Dismissed;

    public event TypedEventHandler<FontFamilyPickerFlyout, PinnableLibraryItem> Pinned;

    public event TypedEventHandler<FontFamilyPickerFlyout, PinnableLibraryItem> Unpinned;

    protected override Control CreatePresenter()
    {
        var pfp = new LibraryItemPickerFlyoutPresenter();

        pfp.CloseClicked += (_, _) => Hide();
        pfp.Confirmed += OnFlyoutConfirmed;
        pfp.Dismissed += OnFlyoutDismissed;
        pfp.Pinned += item => Pinned?.Invoke(this, item);
        pfp.Unpinned += item => Unpinned?.Invoke(this, item);
        pfp.Items = viewModel.Items;
        pfp.SelectedItem = viewModel.SelectedItem.Value;
        pfp.GetObservable(LibraryItemPickerFlyoutPresenter.SelectedItemProperty)
            .Subscribe(v => viewModel.SelectedItem.Value = v);
        pfp.GetObservable(LibraryItemPickerFlyoutPresenter.ShowAllProperty)
            .Subscribe(v => viewModel.ShowAll.Value = v);
        pfp.GetObservable(LibraryItemPickerFlyoutPresenter.SearchTextProperty)
            .Subscribe(v => viewModel.SearchText.Value = v);
        pfp.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter:
                    OnConfirmed();
                    break;
                case Key.Escape:
                    Hide();
                    break;
            }
        };

        return pfp;
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
