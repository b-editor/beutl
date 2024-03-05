using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Input;

using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.Controls.PropertyEditors;

public sealed class SimpleColorPickerFlyout : PickerFlyoutBase
{
    public SimpleColorPicker ColorPicker => _picker ??= new SimpleColorPicker();

    public event TypedEventHandler<SimpleColorPickerFlyout, EventArgs> Confirmed;

    public event TypedEventHandler<SimpleColorPickerFlyout, EventArgs> Dismissed;

    public event TypedEventHandler<SimpleColorPickerFlyout, EventArgs> CloseClicked;

    protected override Control CreatePresenter()
    {
        var pfp = new SimpleColorPickerFlyoutPresenter()
        {
            Content = ColorPicker
        };
        pfp.Confirmed += OnFlyoutConfirmed;
        pfp.Dismissed += OnFlyoutDismissed;
        pfp.CloseClicked += OnFlyoutCloseClicked;
        pfp.KeyDown += OnFlyoutKeyDown;

        return pfp;
    }

    private void OnFlyoutKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
        {
            if (_showButtons)
            {
                if (e.Key == Key.Enter)
                {
                    Confirmed?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    Dismissed?.Invoke(this, EventArgs.Empty);
                }
            }
            else
            {
                CloseClicked?.Invoke(this, EventArgs.Empty);
            }

            Hide();
        }
    }

    protected override void OnConfirmed()
    {
        Confirmed?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    protected override void OnOpening(CancelEventArgs args)
    {
        base.OnOpening(args);

        if (Popup.Child is DraggablePickerFlyoutPresenter pfp)
        {
            pfp.ShowHideButtons = ShouldShowConfirmationButtons();
        }

        Popup.IsLightDismissEnabled = false;
    }

    protected override bool ShouldShowConfirmationButtons() => _showButtons;

    private void OnFlyoutCloseClicked(DraggablePickerFlyoutPresenter sender, EventArgs args)
    {
        CloseClicked?.Invoke(this, EventArgs.Empty);
        Hide();
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

    internal void ShowHideButtons(bool show)
    {
        _showButtons = show;
    }

    private bool _showButtons = true;
    private SimpleColorPicker _picker;
}
