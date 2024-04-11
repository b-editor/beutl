using System.ComponentModel;
using Avalonia;
using Avalonia.Input;
using Avalonia.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.ViewModels.Dialogs;
using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls.Primitives;
using Reactive.Bindings.Extensions;

namespace Beutl.Views.Editors;

public sealed class LibraryItemPickerFlyout(SelectLibraryItemDialogViewModel viewModel) : PickerFlyoutBase
{
    public event TypedEventHandler<LibraryItemPickerFlyout, EventArgs>? Confirmed;

    public event TypedEventHandler<LibraryItemPickerFlyout, EventArgs>? Dismissed;

    public event TypedEventHandler<LibraryItemPickerFlyout, PinnableLibraryItem>? Pinned;

    public event TypedEventHandler<LibraryItemPickerFlyout, PinnableLibraryItem>? Unpinned;

    protected override Control CreatePresenter()
    {
        var pfp = new LibraryItemPickerFlyoutPresenter();
        pfp.CloseClicked += (_, _) => Hide();
        pfp.Confirmed += OnFlyoutConfirmed;
        pfp.Dismissed += OnFlyoutDismissed;
        pfp.Pinned += item => Pinned?.Invoke(this, item);
        pfp.Unpinned += item => Unpinned?.Invoke(this, item);
        pfp.Items = viewModel.Items;
        pfp.GetObservable(LibraryItemPickerFlyoutPresenter.SelectedItemProperty)
            .Subscribe(v => viewModel.SelectedItem.Value = v);
        pfp.GetObservable(LibraryItemPickerFlyoutPresenter.ShowAllProperty)
            .Subscribe(v => viewModel.ShowAll.Value = v);
        pfp.GetObservable(LibraryItemPickerFlyoutPresenter.SearchTextProperty)
            .Subscribe(v => viewModel.SearchText.Value = v);
        viewModel.IsBusy.ObserveOnUIDispatcher()
            .Subscribe(v => pfp.IsBusy = v);
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

        Popup.IsLightDismissEnabled = false;
    }

    protected override bool ShouldShowConfirmationButtons() => true;
}
