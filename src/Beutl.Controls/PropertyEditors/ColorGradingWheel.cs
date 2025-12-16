#nullable enable

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Beutl.Media;

namespace Beutl.Controls.PropertyEditors;

public class ColorGradingWheel : PropertyEditor
{
    public static readonly StyledProperty<GradingColor> ColorProperty =
        AvaloniaProperty.Register<ColorGradingWheel, GradingColor>(nameof(Color), new GradingColor(1, 1, 1),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<GradingColorPickerInputType> InputTypeProperty =
        AvaloniaProperty.Register<ColorGradingWheel, GradingColorPickerInputType>(nameof(InputType));

    public static readonly StyledProperty<bool> ShowDetailsProperty =
        AvaloniaProperty.Register<ColorGradingWheel, bool>(nameof(ShowDetails));

    private GradingColorPicker? _gradingColorPicker;

    public GradingColor Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public GradingColorPickerInputType InputType
    {
        get => GetValue(InputTypeProperty);
        set => SetValue(InputTypeProperty, value);
    }

    public bool ShowDetails
    {
        get => GetValue(ShowDetailsProperty);
        set => SetValue(ShowDetailsProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_gradingColorPicker != null)
        {
            _gradingColorPicker.ColorChanged -= OnColorChanged;
            _gradingColorPicker.ColorConfirmed -= OnColorConfirmed;
        }

        _gradingColorPicker = e.NameScope.Find<GradingColorPicker>("PART_GradingColorPicker");

        if (_gradingColorPicker != null)
        {
            _gradingColorPicker.ColorChanged += OnColorChanged;
            _gradingColorPicker.ColorConfirmed += OnColorConfirmed;
        }
    }

    private void OnColorConfirmed(GradingColorPicker sender, (GradingColor OldValue, GradingColor NewValue) args)
    {
        RaiseEvent(new PropertyEditorValueChangedEventArgs<GradingColor>(
            args.NewValue, args.OldValue, ValueConfirmedEvent));
    }

    private void OnColorChanged(GradingColorPicker sender, (GradingColor OldValue, GradingColor NewValue) args)
    {
        RaiseEvent(new PropertyEditorValueChangedEventArgs<GradingColor>(
            args.NewValue, args.OldValue, ValueChangedEvent));
    }
}
