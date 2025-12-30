using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;

using Beutl.Media;

#nullable enable

namespace Beutl.Controls.PropertyEditors;

public class GradingColorEditor : PropertyEditor
{
    public static readonly DirectProperty<GradingColorEditor, GradingColor> ValueProperty =
        AvaloniaProperty.RegisterDirect<GradingColorEditor, GradingColor>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsLivePreviewEnabledProperty =
        AvaloniaProperty.Register<GradingColorEditor, bool>(nameof(IsLivePreviewEnabled));

    private GradingColorPickerFlyout? _flyout;

    private bool _flyoutActive;
    private Button? _button;

    private GradingColor _oldValue;
    private GradingColor _value;

    public GradingColor Value
    {
        get => _value;
        set => SetAndRaise(ValueProperty, ref _value, value);
    }

    public bool IsLivePreviewEnabled
    {
        get => GetValue(IsLivePreviewEnabledProperty);
        set => SetValue(IsLivePreviewEnabledProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
        {
            _flyout?.Color = Value;
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_button != null)
        {
            _button.Click -= OnButtonClick;
        }
        base.OnApplyTemplate(e);
        _button = e.NameScope.Get<Button>("PART_ColorPickerButton");
        if (_button != null)
        {
            _button.Click += OnButtonClick;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _flyout ??= new GradingColorPickerFlyout();
        _flyout.Closed += OnFlyoutClosed;
        _flyout.Confirmed += OnFlyoutConfirmed;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_flyout != null)
        {
            _flyout.Closed -= OnFlyoutClosed;
            _flyout.Confirmed -= OnFlyoutConfirmed;
        }
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_flyout == null) return;

        _flyout.Hide();

        _flyout.Color = Value;

        _flyout.Placement = PlacementMode.Bottom;

        _flyout.ShowHideButtons(!IsLivePreviewEnabled);

        if (IsLivePreviewEnabled)
        {
            _flyout.ColorChanged += OnFlyoutColorChanged;
        }

        _flyout.ShowAt(this);

        _flyoutActive = true;

        if (IsLivePreviewEnabled)
            _oldValue = _value;
    }

    private void OnFlyoutColorChanged(GradingColorPickerFlyout sender, (GradingColor OldValue, GradingColor NewValue) args)
    {
        if (IsLivePreviewEnabled)
        {
            var oldValue = Value;
            Value = args.NewValue;
            RaiseEvent(new PropertyEditorValueChangedEventArgs<GradingColor>(
                args.NewValue, oldValue, ValueChangedEvent));
        }
    }

    private void OnFlyoutConfirmed(GradingColorPickerFlyout sender, EventArgs args)
    {
        if (_flyoutActive && _flyout != null)
        {
            Value = _flyout.Color;
            RaiseEvent(new PropertyEditorValueChangedEventArgs<GradingColor>(
                Value, _oldValue, ValueConfirmedEvent));
        }
    }

    private void OnFlyoutClosed(object? sender, EventArgs e)
    {
        if (_flyout != null && IsLivePreviewEnabled)
        {
            _flyout.ColorChanged -= OnFlyoutColorChanged;
            RaiseEvent(new PropertyEditorValueChangedEventArgs<GradingColor>(
                Value, _oldValue, ValueConfirmedEvent));
        }

        _flyoutActive = false;
    }
}
