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

    public event EventHandler<string?>? Confirmed;

    protected override Control CreatePresenter()
    {
        _textBox ??= new TextBox { Watermark = Strings.EnterTemplateName };
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
        _textBox.Text = string.Empty;
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
