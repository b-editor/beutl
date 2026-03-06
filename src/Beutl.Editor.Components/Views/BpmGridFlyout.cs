using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Beutl.ProjectSystem;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.Editor.Components.Views;

public sealed class BpmGridFlyout : PickerFlyoutBase
{
    public static readonly StyledProperty<bool> IsEnabledCheckedProperty
        = AvaloniaProperty.Register<BpmGridFlyout, bool>(nameof(IsEnabledChecked));

    public static readonly StyledProperty<decimal> BpmProperty
        = AvaloniaProperty.Register<BpmGridFlyout, decimal>(nameof(Bpm), 120m);

    public static readonly StyledProperty<int> SubdivisionsProperty
        = AvaloniaProperty.Register<BpmGridFlyout, int>(nameof(Subdivisions), 4);

    public static readonly StyledProperty<decimal> OffsetSecondsProperty
        = AvaloniaProperty.Register<BpmGridFlyout, decimal>(nameof(OffsetSeconds));

    private ToggleSwitch? _enabledToggle;
    private NumericUpDown? _bpmUpDown;
    private ComboBox? _subdivisionCombo;
    private NumericUpDown? _offsetUpDown;
    private BpmGridOptions _initialOptions;

    public bool IsEnabledChecked
    {
        get => GetValue(IsEnabledCheckedProperty);
        set => SetValue(IsEnabledCheckedProperty, value);
    }

    public decimal Bpm
    {
        get => GetValue(BpmProperty);
        set => SetValue(BpmProperty, value);
    }

    public int Subdivisions
    {
        get => GetValue(SubdivisionsProperty);
        set => SetValue(SubdivisionsProperty, value);
    }

    public decimal OffsetSeconds
    {
        get => GetValue(OffsetSecondsProperty);
        set => SetValue(OffsetSecondsProperty, value);
    }

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

        var pfp = new PickerFlyoutPresenter
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
        _initialOptions = new BpmGridOptions(
            (double)Bpm, Subdivisions,
            TimeSpan.FromSeconds((double)OffsetSeconds), IsEnabledChecked);

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

    private void OnFlyoutConfirmed(PickerFlyoutPresenter sender, object args)
    {
        OnConfirmed();
    }

    private void OnFlyoutDismissed(PickerFlyoutPresenter sender, object args)
    {
        IsEnabledChecked = _initialOptions.IsEnabled;
        Bpm = (decimal)_initialOptions.Bpm;
        Subdivisions = _initialOptions.Subdivisions;
        OffsetSeconds = (decimal)_initialOptions.Offset.TotalSeconds;
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
        var options = new BpmGridOptions(
            (double)Bpm,
            Subdivisions,
            TimeSpan.FromSeconds((double)OffsetSeconds),
            IsEnabledChecked);
        OptionsChanged?.Invoke(this, options);
    }
}
