using System.ComponentModel;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.Controls;

// A flyout with a header over a text box; Confirmed passes the text when the flyout is confirmed.
internal abstract class TextInputFlyout : FAPickerFlyoutBase
{
    private readonly string _header;
    private readonly double _width;
    private readonly string? _placeholderText;
    private readonly bool _selectAllOnOpening;
    private TextBox? _textBox;

    private protected TextInputFlyout(string header, double width, string? placeholderText, bool selectAllOnOpening)
    {
        _header = header;
        _width = width;
        _placeholderText = placeholderText;
        _selectAllOnOpening = selectAllOnOpening;
    }

    public string? Text { get; set; }

    public event EventHandler<string?>? Confirmed;

    protected override Control CreatePresenter()
    {
        _textBox ??= CreateTextBox();
        var pfp = new FAPickerFlyoutPresenter()
        {
            Width = _width,
            Padding = new(8, 4),
            Content = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text = _header
                    },
                    _textBox
                }
            }
        };
        pfp.Confirmed += OnFlyoutConfirmed;
        pfp.Dismissed += OnFlyoutDismissed;

        return pfp;
    }

    protected override void OnConfirmed()
    {
        Confirmed?.Invoke(this, _textBox?.Text);
        Hide();
    }

    protected override void OnOpening(CancelEventArgs args)
    {
        base.OnOpening(args);
        _textBox ??= CreateTextBox();
        _textBox.Text = Text;
        if (_selectAllOnOpening)
        {
            _textBox.SelectAll();
            _textBox.Focus();
        }
    }

    private TextBox CreateTextBox() => new() { PlaceholderText = _placeholderText };

    private void OnFlyoutDismissed(FAPickerFlyoutPresenter sender, object args)
    {
        Hide();
    }

    private void OnFlyoutConfirmed(FAPickerFlyoutPresenter sender, object args)
    {
        OnConfirmed();
    }
}
