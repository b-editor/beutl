using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Media;

using FluentAvalonia.UI.Controls;

namespace Beutl.Controls.PropertyEditors;

public class ColorEditor : PropertyEditor
{
    public static readonly DirectProperty<ColorEditor, Color> ValueProperty =
        AvaloniaProperty.RegisterDirect<ColorEditor, Color>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v,
            defaultBindingMode: BindingMode.TwoWay);

    private Color _value;

    public Color Value
    {
        get => _value;
        set => SetAndRaise(ValueProperty, ref _value, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        ColorPickerButton button = e.NameScope.Get<ColorPickerButton>("PART_ColorPickerButton");
        button.ColorChanged += OnColorChanged;
        button.FlyoutConfirmed += OnFlyoutConfirmed;
    }

    private void OnFlyoutConfirmed(ColorPickerButton sender, ColorButtonColorChangedEventArgs args)
    {
        Value = args.NewColor.GetValueOrDefault();
        RaiseEvent(new PropertyEditorValueChangedEventArgs<Color>(
            Value, args.OldColor.GetValueOrDefault(), ValueChangedEvent));
    }

    private void OnColorChanged(ColorPickerButton sender, ColorButtonColorChangedEventArgs args)
    {
        RaiseEvent(new PropertyEditorValueChangedEventArgs<Color>(
            args.NewColor.GetValueOrDefault(), args.OldColor.GetValueOrDefault(), ValueChangingEvent));
    }
}
