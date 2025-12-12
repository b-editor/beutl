using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Media;
using FluentAvalonia.UI.Media;

namespace Beutl.Controls.PropertyEditors;

public class ColorWheelEditor : PropertyEditor
{
    public static readonly DirectProperty<ColorWheelEditor, Color> ValueProperty =
        AvaloniaProperty.RegisterDirect<ColorWheelEditor, Color>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsLivePreviewEnabledProperty =
        AvaloniaProperty.Register<ColorWheelEditor, bool>(nameof(IsLivePreviewEnabled));

    private ColorWheelPicker? _picker;
    private Color _oldValue;
    private Color _value;
    private bool _ignoreValueUpdate;

    public Color Value
    {
        get => _value;
        set
        {
            if (_ignoreValueUpdate)
                return;

            SetAndRaise(ValueProperty, ref _value, value);
            var color2 = (Color2)value;
            if (_picker != null && _picker.Color != color2)
            {
                _picker.Color = color2;
            }
        }
    }

    public bool IsLivePreviewEnabled
    {
        get => GetValue(IsLivePreviewEnabledProperty);
        set => SetValue(IsLivePreviewEnabledProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_picker != null)
        {
            _picker.ColorChanged -= OnColorChanged;
            _picker.ColorConfirmed -= OnColorConfirmed;
        }

        base.OnApplyTemplate(e);
        _picker = e.NameScope.Get<ColorWheelPicker>("PART_ColorWheelPicker");
        if (_picker != null)
        {
            _picker.Color = Value;
            _picker.ColorChanged += OnColorChanged;
            _picker.ColorConfirmed += OnColorConfirmed;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValueProperty && _picker != null)
        {
            _picker.Color = (Color)change.NewValue!;
        }
    }

    private void OnColorChanged(SimpleColorPicker sender, (Color2 OldValue, Color2 NewValue) args)
    {
        _ignoreValueUpdate = true;
        try
        {
            Value = args.NewValue;
            if (IsLivePreviewEnabled)
            {
                RaiseEvent(new PropertyEditorValueChangedEventArgs<Color>(
                    args.NewValue,
                    args.OldValue,
                    ValueChangedEvent));
            }
        }
        finally
        {
            _ignoreValueUpdate = false;
        }
    }

    private void OnColorConfirmed(SimpleColorPicker sender, (Color2 OldValue, Color2 NewValue) args)
    {
        _oldValue = args.OldValue;
        Value = args.NewValue;

        RaiseEvent(new PropertyEditorValueChangedEventArgs<Color>(
            Value,
            _oldValue,
            ValueConfirmedEvent));
    }
}

