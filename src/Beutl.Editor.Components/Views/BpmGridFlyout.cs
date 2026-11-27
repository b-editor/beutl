using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Beutl.ProjectSystem;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.Editor.Components.Views;

public sealed partial class BpmGridFlyout : FAPickerFlyoutBase
{
    private ToggleSwitch? _enabledToggle;
    private NumericUpDown? _bpmUpDown;
    private ComboBox? _subdivisionCombo;
    private NumericUpDown? _offsetUpDown;
    private BpmGridOptions _initialOptions;

    public static readonly StyledProperty<decimal> BpmProperty =
        AvaloniaProperty.Register<BpmGridFlyout, decimal>(nameof(Bpm), 120m);

    [GeneratedStyledProperty]
    public partial bool IsEnabledChecked { get; set; }

    public decimal Bpm
    {
        get => GetValue(BpmProperty);
        set => SetValue(BpmProperty, value);
    }

    [GeneratedStyledProperty(DefaultValue = 4)]
    public partial int Subdivisions { get; set; }

    [GeneratedStyledProperty]
    public partial decimal OffsetSeconds { get; set; }

    public event EventHandler<BpmGridOptions>? OptionsChanged;

    protected override Control CreatePresenter()
    {
        _enabledToggle = new ToggleSwitch
        {
            IsChecked = IsEnabledChecked,
            OnContent = Strings.ShowBpmGrid,
            OffContent = Strings.ShowBpmGrid,
        };
        _enabledToggle.IsCheckedChanged += (_, _) => RaiseOptionsChanged();

        _bpmUpDown = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 999,
            Value = Bpm,
            Increment = 1,
            FormatString = "F1",
            MinWidth = 140,
        };
        _bpmUpDown.ValueChanged += (_, _) => RaiseOptionsChanged();

        _subdivisionCombo = new ComboBox
        {
            ItemsSource = new[] { 1, 2, 3, 4, 6, 8 },
            SelectedItem = Subdivisions,
            MinWidth = 140,
        };
        _subdivisionCombo.SelectionChanged += (_, _) => RaiseOptionsChanged();

        _offsetUpDown = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 600,
            Value = OffsetSeconds,
            Increment = 0.01m,
            FormatString = "F3",
            MinWidth = 140,
        };
        _offsetUpDown.ValueChanged += (_, _) => RaiseOptionsChanged();

        var content = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(4),
            Children =
            {
                _enabledToggle,
                new TextBlock { Text = Strings.BpmValue },
                _bpmUpDown,
                new TextBlock { Text = Strings.BeatSubdivisions },
                _subdivisionCombo,
                new TextBlock { Text = Strings.BpmOffset },
                _offsetUpDown,
            }
        };

        var pfp = new FAPickerFlyoutPresenter
        {
            Width = 240,
            Padding = new Thickness(8),
            Content = content,
        };
        pfp.Confirmed += OnFlyoutConfirmed;
        pfp.Dismissed += OnFlyoutDismissed;

        return pfp;
    }

    protected override void OnOpening(CancelEventArgs args)
    {
        base.OnOpening(args);
        _initialOptions = CurrentOptions();

        if (_enabledToggle != null)
            _enabledToggle.IsChecked = IsEnabledChecked;
        if (_bpmUpDown != null)
            _bpmUpDown.Value = Bpm;
        if (_subdivisionCombo != null)
            _subdivisionCombo.SelectedItem = Subdivisions;
        if (_offsetUpDown != null)
            _offsetUpDown.Value = OffsetSeconds;
    }

    protected override void OnConfirmed()
    {
        SyncPropertiesFromControls();
        Hide();
    }

    protected override bool ShouldShowConfirmationButtons() => true;

    private void OnFlyoutConfirmed(FAPickerFlyoutPresenter sender, object args)
    {
        OnConfirmed();
    }

    private void OnFlyoutDismissed(FAPickerFlyoutPresenter sender, object args)
    {
        IsEnabledChecked = _initialOptions.IsEnabled;
        Bpm = (decimal)_initialOptions.Bpm;
        Subdivisions = _initialOptions.Subdivisions;
        OffsetSeconds = (decimal)_initialOptions.Offset.ToSeconds();
        OptionsChanged?.Invoke(this, _initialOptions);
        Hide();
    }

    private void SyncPropertiesFromControls()
    {
        if (_enabledToggle != null)
            IsEnabledChecked = _enabledToggle.IsChecked == true;
        if (_bpmUpDown?.Value != null)
            Bpm = _bpmUpDown.Value.Value;
        if (_subdivisionCombo?.SelectedItem is int sub)
            Subdivisions = sub;
        if (_offsetUpDown?.Value != null)
            OffsetSeconds = _offsetUpDown.Value.Value;
    }

    private void RaiseOptionsChanged()
    {
        SyncPropertiesFromControls();
        var options = CurrentOptions();
        OptionsChanged?.Invoke(this, options);
    }

    private BpmGridOptions CurrentOptions()
    {
        return new BpmGridOptions(
            (double)Bpm,
            Subdivisions,
            MediaTime.FromSeconds((double)OffsetSeconds),
            IsEnabledChecked);
    }
}
