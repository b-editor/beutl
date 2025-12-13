using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Input;

using Beutl.Media;

using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls.Primitives;

#nullable enable

namespace Beutl.Controls.PropertyEditors;

public sealed class GradingColorPickerFlyout : PickerFlyoutBase
{
    private GradingColorPicker? _picker;
    private bool _showButtons = true;

    public GradingColorPicker ColorPicker => _picker ??= new GradingColorPicker();

    public GradingColor Color
    {
        get => ColorPicker.Color;
        set => ColorPicker.Color = value;
    }

    public event TypedEventHandler<GradingColorPickerFlyout, EventArgs>? Confirmed;

    public event TypedEventHandler<GradingColorPickerFlyout, EventArgs>? Dismissed;

    public event TypedEventHandler<GradingColorPickerFlyout, EventArgs>? CloseClicked;

    public event TypedEventHandler<GradingColorPickerFlyout, (GradingColor OldValue, GradingColor NewValue)>? ColorChanged;

    public event TypedEventHandler<GradingColorPickerFlyout, (GradingColor OldValue, GradingColor NewValue)>? ColorConfirmed;

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

        ColorPicker.ColorChanged += OnPickerColorChanged;
        ColorPicker.ColorConfirmed += OnPickerColorConfirmed;

        return pfp;
    }

    private void OnPickerColorChanged(GradingColorPicker sender, (GradingColor OldValue, GradingColor NewValue) args)
    {
        ColorChanged?.Invoke(this, args);
    }

    private void OnPickerColorConfirmed(GradingColorPicker sender, (GradingColor OldValue, GradingColor NewValue) args)
    {
        ColorConfirmed?.Invoke(this, args);
    }

    private void OnFlyoutKeyDown(object? sender, KeyEventArgs e)
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
}
