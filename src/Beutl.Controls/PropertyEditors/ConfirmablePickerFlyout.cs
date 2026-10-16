using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.Controls.PropertyEditors;

// A picker flyout confirmed by Enter or the checkmark and dismissed by Escape or the close button. A click outside
// does not dismiss it. Derived flyouts create the presenter and connect it to their view model.
internal abstract class ConfirmablePickerFlyout<TSelf, TPresenter> : FAPickerFlyoutBase
    where TSelf : ConfirmablePickerFlyout<TSelf, TPresenter>
    where TPresenter : DraggablePickerFlyoutPresenter
{
    public event TypedEventHandler<TSelf, EventArgs>? Confirmed;

    public event TypedEventHandler<TSelf, EventArgs>? Dismissed;

    protected abstract TPresenter CreatePickerPresenter();

    protected sealed override Control CreatePresenter()
    {
        TPresenter presenter = CreatePickerPresenter();
        presenter.CloseClicked += (_, _) => Dismiss();
        presenter.Confirmed += (_, _) => OnConfirmed();
        presenter.Dismissed += (_, _) => Dismiss();
        presenter.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter:
                    OnConfirmed();
                    break;
                case Key.Escape:
                    Dismiss();
                    break;
            }
        };
        return presenter;
    }

    protected override void OnOpened()
    {
        base.OnOpened();
        if (Popup.Child is TPresenter presenter)
        {
            presenter.FocusInitialElement();
        }
    }

    protected override void OnConfirmed()
    {
        Confirmed?.Invoke((TSelf)this, EventArgs.Empty);
        Hide();
    }

    protected override void OnOpening(CancelEventArgs args)
    {
        base.OnOpening(args);
        Popup.IsLightDismissEnabled = false;
    }

    protected override bool ShouldShowConfirmationButtons() => true;

    private void Dismiss()
    {
        Dismissed?.Invoke((TSelf)this, EventArgs.Empty);
        Hide();
    }
}
