using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Beutl.Language;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

internal sealed class KeyFrameValueScaleFlyout : FAPickerFlyoutBase
{
    private readonly string _channel;
    private readonly NumericUpDown _factor = new()
    {
        Name = "ScaleFactor",
        Value = 1,
        Minimum = -1000000,
        Maximum = 1000000,
        Increment = 0.1m,
        FormatString = "0.###"
    };

    public KeyFrameValueScaleFlyout(string channel)
    {
        _channel = channel;
        Placement = PlacementMode.Top;
        AutomationProperties.SetName(_factor, Strings.GraphScaleFactor);
    }

    public double Factor => (double)(_factor.Value ?? 1);

    public bool IsConfirmed { get; private set; }

    protected override Control CreatePresenter()
    {
        var presenter = new FAPickerFlyoutPresenter
        {
            Width = 260,
            Padding = new Thickness(12),
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = $"{Strings.GraphScaleValues} ({_channel})" },
                    new TextBlock { Text = Strings.GraphScaleFactor },
                    _factor
                }
            }
        };
        presenter.Confirmed += (_, _) => OnConfirmed();
        presenter.Dismissed += (_, _) => Hide();
        presenter.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Hide();
        };
        return presenter;
    }

    protected override void OnConfirmed()
    {
        if (!IsOpen || _factor.Value == null) return;
        IsConfirmed = true;
        Hide();
    }

    protected override bool ShouldShowConfirmationButtons() => true;
}
