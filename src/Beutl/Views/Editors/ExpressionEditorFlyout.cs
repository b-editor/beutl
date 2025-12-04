using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Beutl.Controls.PropertyEditors;
using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.Views.Editors;

public sealed class ExpressionEditorFlyout : PickerFlyoutBase
{
    private TextBox? _textBox;
    private TextBlock? _errorTextBlock;

    public string? ExpressionText
    {
        get => _textBox?.Text;
        set
        {
            if (_textBox != null)
            {
                _textBox.Text = value;
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorTextBlock?.Text;
        set
        {
            if (_errorTextBlock != null)
            {
                _errorTextBlock.Text = value;
                _errorTextBlock.IsVisible = !string.IsNullOrWhiteSpace(value);
            }
        }
    }

    public Func<string, (bool IsValid, string? Error)>? Validator { get; set; }

    public event TypedEventHandler<ExpressionEditorFlyout, EventArgs>? Confirmed;

    public event TypedEventHandler<ExpressionEditorFlyout, EventArgs>? Dismissed;

    protected override Control CreatePresenter()
    {
        _textBox = new TextBox
        {
            Watermark = "Sin(Time * 2 * PI) * 100",
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MinHeight = 100,
            MaxHeight = 200,
            VerticalContentAlignment = VerticalAlignment.Top
        };

        _errorTextBlock = new TextBlock
        {
            Foreground = Avalonia.Application.Current!.FindResource("SystemFillColorCriticalBrush") as IBrush,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            IsVisible = false
        };

        var content = new StackPanel
        {
            Width = 300,
            Spacing = 8,
            Margin = new Avalonia.Thickness(8, 40, 8, 8),
            Children =
            {
                new TextBlock
                {
                    Text = Language.Strings.ExpressionHelp,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                _textBox,
                _errorTextBlock
            }
        };

        var presenter = new DraggablePickerFlyoutPresenter
        {
            Content = content
        };

        presenter.Confirmed += OnFlyoutConfirmed;
        presenter.Dismissed += OnFlyoutDismissed;
        presenter.CloseClicked += OnFlyoutCloseClicked;
        presenter.KeyDown += OnFlyoutKeyDown;

        return presenter;
    }

    private void OnFlyoutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
        {
            TryConfirm();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Dismissed?.Invoke(this, EventArgs.Empty);
            Hide();
            e.Handled = true;
        }
    }

    private bool TryConfirm()
    {
        string expressionText = ExpressionText ?? "";

        if (!string.IsNullOrWhiteSpace(expressionText) && Validator != null)
        {
            var (isValid, error) = Validator(expressionText);
            if (!isValid)
            {
                ErrorMessage = error;
                return false;
            }
        }

        ErrorMessage = null;
        OnConfirmed();
        return true;
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
        TryConfirm();
    }
}
