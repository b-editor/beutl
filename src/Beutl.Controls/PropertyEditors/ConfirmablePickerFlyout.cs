using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.Controls.PropertyEditors;

// A picker flyout confirmed by Enter or the checkmark and dismissed by Escape or the close button. A click outside
// does not dismiss it. Derived flyouts create the presenter and connect it to their view model.
public abstract class ConfirmablePickerFlyout<TSelf, TPresenter> : FAPickerFlyoutBase
    where TSelf : ConfirmablePickerFlyout<TSelf, TPresenter>
    where TPresenter : DraggablePickerFlyoutPresenter
{
    private bool _confirmed;

    public event TypedEventHandler<TSelf, EventArgs>? Confirmed;

    // Raised when the picker closes any way but by confirming, even with the control it is placed at.
    public event TypedEventHandler<TSelf, EventArgs>? Dismissed;

    protected abstract TPresenter CreatePickerPresenter();

    // A flyout whose search waits for a typing pause applies what was typed so far here.
    protected virtual void FlushPendingSearch()
    {
    }

    protected sealed override Control CreatePresenter()
    {
        TPresenter presenter = CreatePickerPresenter();
        presenter.CloseClicked += (_, _) => Hide();
        presenter.Confirmed += (_, _) => OnConfirmed();
        presenter.Dismissed += (_, _) => Hide();
        presenter.KeyDown += (_, e) =>
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
        presenter.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            // These move through the results, so show the results for everything typed so far first.
            if (e.Key is Key.Up or Key.Down)
                FlushPendingSearch();
        }, RoutingStrategies.Tunnel);
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
        // Enter, the checkmark and any other way of confirming pick from the results for everything typed.
        FlushPendingSearch();
        _confirmed = true;
        Confirmed?.Invoke((TSelf)this, EventArgs.Empty);
        Hide();
    }

    protected override void OnClosed()
    {
        base.OnClosed();
        if (!_confirmed)
        {
            Dismissed?.Invoke((TSelf)this, EventArgs.Empty);
        }

        _confirmed = false;
    }

    protected override void OnOpening(CancelEventArgs args)
    {
        base.OnOpening(args);
        Popup.IsLightDismissEnabled = false;
    }

    protected override bool ShouldShowConfirmationButtons() => true;
}
