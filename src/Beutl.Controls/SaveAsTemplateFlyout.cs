#nullable enable

using System.ComponentModel;
using Avalonia.Controls;
using Beutl.Language;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.Controls;

public sealed class SaveAsTemplateFlyout : PickerFlyoutBase
{
    private TextBox? _textBox;
    private string _text = string.Empty;

    public event EventHandler<string?>? Confirmed;

    public string Text
    {
        get => _textBox?.Text ?? _text;
        set
        {
            _text = value ?? string.Empty;
            if (_textBox != null)
                _textBox.Text = _text;
        }
    }

    protected override Control CreatePresenter()
    {
        _textBox ??= new TextBox { Watermark = Strings.EnterTemplateName };
        _textBox.Text = _text;
        var pfp = new PickerFlyoutPresenter()
        {
            Width = 280,
            Padding = new(8, 4),
            Content = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text = Strings.SaveAsTemplate
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
        _textBox ??= new TextBox { Watermark = Strings.EnterTemplateName };
        _textBox.Text = _text;
        _textBox.SelectAll();
        _textBox.Focus();
    }

    private void OnFlyoutDismissed(PickerFlyoutPresenter sender, object args)
    {
        Hide();
    }

    private void OnFlyoutConfirmed(PickerFlyoutPresenter sender, object args)
    {
        OnConfirmed();
    }
}
