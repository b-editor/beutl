using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Beutl.Controls.PropertyEditors;
using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.Views.Editors;

public sealed class ExpressionEditorFlyout : PickerFlyoutBase
{
    private ExpressionEditorFlyoutPresenter? _presenter;

    public string? ExpressionText
    {
        get => _presenter?.ExpressionText ?? field;
        set
        {
            field = value;
            if (_presenter != null)
            {
                _presenter.ExpressionText = value;
            }
        }
    }

    public string? ErrorMessage
    {
        get => _presenter?.ErrorMessage ?? field;
        set
        {
            field = value;
            if (_presenter != null)
            {
                _presenter.ErrorMessage = value;
            }
        }
    }

    public event TypedEventHandler<ExpressionEditorFlyout, ExpressionConfirmedEventArgs>? Confirmed;

    public event TypedEventHandler<ExpressionEditorFlyout, EventArgs>? Dismissed;

    protected override Control CreatePresenter()
    {
        _presenter = new ExpressionEditorFlyoutPresenter();
        _presenter.ExpressionText = ExpressionText;
        _presenter.ErrorMessage = ErrorMessage;
        _presenter.Confirmed += OnFlyoutConfirmed;
        _presenter.Dismissed += OnFlyoutDismissed;
        _presenter.CloseClicked += OnFlyoutCloseClicked;
        _presenter.KeyDown += OnFlyoutKeyDown;

        return _presenter;
    }

    private void OnFlyoutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
        {
            OnConfirmed();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Dismissed?.Invoke(this, EventArgs.Empty);
            Hide();
            e.Handled = true;
        }
    }

    protected override void OnConfirmed()
    {
        string expressionText = ExpressionText ?? "";

        if (!string.IsNullOrWhiteSpace(expressionText))
        {
            var args = new ExpressionConfirmedEventArgs(expressionText);
            Confirmed?.Invoke(this, args);

            if (!args.IsValid)
            {
                ErrorMessage = args.Error;
                return;
            }
        }

        ErrorMessage = null;
        Hide();
    }

    protected override void OnOpening(CancelEventArgs args)
    {
        base.OnOpening(args);

        if (Popup.Child is ExpressionEditorFlyoutPresenter pfp)
        {
            pfp.ShowHideButtons = ShouldShowConfirmationButtons();
        }

        Popup.IsLightDismissEnabled = false;
    }

    protected override bool ShouldShowConfirmationButtons() => true;

    private void OnFlyoutCloseClicked(DraggablePickerFlyoutPresenter sender, EventArgs args)
    {
        Dismissed?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    private void OnFlyoutDismissed(DraggablePickerFlyoutPresenter sender, EventArgs args)
    {
        Dismissed?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    private void OnFlyoutConfirmed(DraggablePickerFlyoutPresenter sender, EventArgs args)
    {
        OnConfirmed();
    }
}

public sealed class ExpressionConfirmedEventArgs : EventArgs
{
    public ExpressionConfirmedEventArgs(string expressionText)
    {
        ExpressionText = expressionText;
    }

    public string ExpressionText { get; }

    public bool IsValid { get; set; } = true;

    public string? Error { get; set; }
}
