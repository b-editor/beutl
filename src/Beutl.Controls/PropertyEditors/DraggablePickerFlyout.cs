using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Input;

using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.Controls.PropertyEditors;

// The flyouts of SimpleColorPicker and GradingColorPicker; they differ only in the picker they host.
public abstract class DraggablePickerFlyout<TPicker> : FAPickerFlyoutBase
    where TPicker : Control, new()
{
    private TPicker? _picker;
    private bool _showButtons = true;

    public TPicker ColorPicker => _picker ??= new TPicker();

    public event TypedEventHandler<DraggablePickerFlyout<TPicker>, EventArgs>? Confirmed;

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

    private void OnFlyoutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
        {
            if (_showButtons && e.Key == Key.Enter)
            {
                Confirmed?.Invoke(this, EventArgs.Empty);
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
        Hide();
    }

    private void OnFlyoutDismissed(DraggablePickerFlyoutPresenter sender, object args)
    {
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
}
