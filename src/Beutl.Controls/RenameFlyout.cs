#nullable enable

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Beutl.Language;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.Controls;

public sealed class RenameFlyout : PickerFlyoutBase
{
    public static readonly StyledProperty<string?> TextProperty
        = TextBox.TextProperty.AddOwner<RenameFlyout>();

    private TextBox? _textBox;

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public event EventHandler<string?>? Confirmed;

    protected override Control CreatePresenter()
    {
        _textBox ??= new TextBox();
        var pfp = new PickerFlyoutPresenter()
        {
            Width = 240,
            Padding = new(8, 4),
            Content = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text = Strings.Rename
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
        _textBox ??= new TextBox();
        _textBox.Text = Text;
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
