using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Beutl.Controls.PropertyEditors;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

internal readonly record struct KeyFrameVelocityValues(
    double IncomingSpeed, double IncomingInfluence, double OutgoingSpeed, double OutgoingInfluence);

internal sealed class KeyFrameVelocityFlyout : FAPickerFlyoutBase
{
    private readonly NumericUpDown _incomingSpeed;
    private readonly NumericUpDown _incomingInfluence;
    private readonly NumericUpDown _outgoingSpeed;
    private readonly NumericUpDown _outgoingInfluence;
    private readonly StackPanel _content;

    public KeyFrameVelocityFlyout(KeyFrameVelocityValues values, bool hasIncoming, bool hasOutgoing)
    {
        Placement = PlacementMode.Top;
        _incomingSpeed = Number("IncomingSpeed", Strings.GraphIncoming, values.IncomingSpeed, -1e12, 1e12);
        _outgoingSpeed = Number("OutgoingSpeed", Strings.GraphOutgoing, values.OutgoingSpeed, -1e12, 1e12);
        _incomingInfluence = Number("IncomingInfluence", $"{Strings.GraphIncoming} / {Strings.GraphInfluence}", values.IncomingInfluence * 100, 0.1, 99.9);
        _outgoingInfluence = Number("OutgoingInfluence", $"{Strings.GraphOutgoing} / {Strings.GraphInfluence}", values.OutgoingInfluence * 100, 0.1, 99.9);
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12 };
        columns.Children.Add(Column(Strings.GraphIncoming, _incomingSpeed, _incomingInfluence, hasIncoming));
        var outgoing = Column(Strings.GraphOutgoing, _outgoingSpeed, _outgoingInfluence, hasOutgoing);
        Grid.SetColumn(outgoing, 1);
        columns.Children.Add(outgoing);
        _content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                columns
            }
        };
        foreach (var number in new[] { _incomingSpeed, _incomingInfluence, _outgoingSpeed, _outgoingInfluence })
            number.ValueChanged += (_, _) =>
            {
                if (IsOpen) ValuesChanged?.Invoke(this, Values);
            };
    }

    public event EventHandler<KeyFrameVelocityValues>? ValuesChanged;

    public bool IsConfirmed { get; private set; }

    public KeyFrameVelocityValues Values => new(
        (double)(_incomingSpeed.Value ?? 0), (double)(_incomingInfluence.Value ?? 33.333m) / 100,
        (double)(_outgoingSpeed.Value ?? 0), (double)(_outgoingInfluence.Value ?? 33.333m) / 100);

    protected override Control CreatePresenter()
    {
        var presenter = new DraggablePickerFlyoutPresenter
        {
            Width = 320,
            Padding = new Thickness(8, 40, 8, 4),
            ShowHideButtons = true,
            Content = _content
        };
        presenter.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<Panel>("DragArea") is { } header)
                header.Children.Add(new TextBlock
                {
                    Text = Strings.GraphVelocityTitle,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(8, 0, 40, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false
                });
            if (e.NameScope.Find<Button>("CloseButton") is { } close) close.IsVisible = true;
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(presenter, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        presenter.Confirmed += (_, _) => OnConfirmed();
        presenter.Dismissed += (_, _) => Hide();
        presenter.CloseClicked += (_, _) => Hide();
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
        if (!IsOpen) return;
        IsConfirmed = true;
        Hide();
    }

    private static StackPanel Column(string label, NumericUpDown speed, NumericUpDown influence, bool enabled) => new()
    {
        Spacing = 6,
        IsEnabled = enabled,
        Children =
        {
            new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, speed,
            new TextBlock { Text = Strings.GraphInfluence, TextWrapping = TextWrapping.Wrap }, influence
        }
    };

    private static NumericUpDown Number(string name, string label, double value, double min, double max)
    {
        var number = new NumericUpDown
        {
            Name = name,
            Value = (decimal)Math.Clamp(value, min, max),
            Minimum = (decimal)min,
            Maximum = (decimal)max,
            Increment = 1,
            FormatString = "0.###",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(number, label);
        return number;
    }
}
