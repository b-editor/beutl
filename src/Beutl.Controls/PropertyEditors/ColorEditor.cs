using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;

using FluentAvalonia.UI.Media;

namespace Beutl.Controls.PropertyEditors;

public class ColorEditor : PropertyEditor
{
    public static readonly DirectProperty<ColorEditor, Color> ValueProperty =
        AvaloniaProperty.RegisterDirect<ColorEditor, Color>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsLivePreviewEnabledProperty =
        AvaloniaProperty.Register<ColorEditor, bool>(nameof(IsLivePreviewEnabled));

    private SimpleColorPickerFlyout _flyout;

    private bool _flyoutActive;
    private Button _button;

    private Color _oldValue;
    private Color _value;

    public Color Value
    {
        get => _value;
        set => SetAndRaise(ValueProperty, ref _value, value);
    }

    public bool IsLivePreviewEnabled
    {
        get => GetValue(IsLivePreviewEnabledProperty);
        set => SetValue(IsLivePreviewEnabledProperty, value);
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
        _flyout ??= new SimpleColorPickerFlyout();
        _flyout.Closed += OnFlyoutClosed;
        _flyout.Confirmed += OnFlyoutConfirmed;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _flyout.Closed -= OnFlyoutClosed;
        _flyout.Confirmed -= OnFlyoutConfirmed;
    }

    private void OnButtonClick(object sender, RoutedEventArgs e)
    {
        _flyout.Hide();

        Color color = Value;
        _flyout.ColorPicker.Color = color;

        _flyout.Placement = PlacementMode.Bottom;

        _flyout.ShowHideButtons(!IsLivePreviewEnabled);

        if (IsLivePreviewEnabled)
        {
            _flyout.ColorPicker.ColorChanged += OnColorPickerColorChanged;
        }

        _flyout.ShowAt(this);

        _flyoutActive = true;

        if (IsLivePreviewEnabled)
            _oldValue = _value;
    }

    private void OnColorPickerColorChanged(SimpleColorPicker sender, (Color2 OldValue, Color2 NewValue) args)
    {
        if (IsLivePreviewEnabled)
        {
            Value = args.NewValue;
            RaiseEvent(new PropertyEditorValueChangedEventArgs<Color>(
                args.NewValue, args.OldValue, ValueChangedEvent));
        }
    }

    private void OnFlyoutConfirmed(SimpleColorPickerFlyout sender, object args)
    {
        if (_flyoutActive)
        {
            Value = _flyout.ColorPicker.Color;
            RaiseEvent(new PropertyEditorValueChangedEventArgs<Color>(
                Value, _oldValue, ValueConfirmedEvent));
        }
    }

    private void OnFlyoutClosed(object sender, EventArgs e)
    {
        if (IsLivePreviewEnabled)
        {
            _flyout.ColorPicker.ColorChanged -= OnColorPickerColorChanged;
            RaiseEvent(new PropertyEditorValueChangedEventArgs<Color>(
                Value, _oldValue, ValueConfirmedEvent));
        }

        _flyoutActive = false;
    }
}
