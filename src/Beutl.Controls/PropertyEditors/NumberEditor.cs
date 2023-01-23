using System.Globalization;
using System.Numerics;

using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Beutl.Controls.PropertyEditors;

public class NumberEditor<TValue> : StringEditor
    where TValue : INumber<TValue>
{
    public static readonly DirectProperty<NumberEditor<TValue>, TValue> ValueProperty =
        AvaloniaProperty.RegisterDirect<NumberEditor<TValue>, TValue>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v,
            defaultBindingMode: BindingMode.TwoWay);
    private TValue _value;
    private TValue _oldValue;

    public TValue Value
    {
        get => _value;
        set
        {
            if (SetAndRaise(ValueProperty, ref _value, value))
            {
                Text = value.ToString();
            }
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        InnerTextBox.AddHandler(PointerWheelChangedEvent, OnTextBoxPointerWheelChanged, RoutingStrategies.Tunnel);
    }

    protected override void OnTextBoxGotFocus(GotFocusEventArgs e)
    {
        _oldValue = Value;
    }

    protected override void OnTextBoxLostFocus(RoutedEventArgs e)
    {
        if (Value != _oldValue)
        {
            RaiseEvent(new PropertyEditorValueChangedEventArgs<TValue>(Value, _oldValue, ValueChangedEvent));
        }
    }

    protected override void OnTextBoxTextChanged(string newValue, string oldValue)
    {
        if (TValue.TryParse(newValue, CultureInfo.CurrentUICulture, out TValue newValue2)
            && TValue.TryParse(oldValue, CultureInfo.CurrentUICulture, out TValue oldValue2)
            && newValue2 != oldValue2)
        {
            Value = newValue2;
            RaiseEvent(new PropertyEditorValueChangedEventArgs<TValue>(newValue2, oldValue2, ValueChangingEvent));
        }
    }

    private void OnTextBoxPointerWheelChanged(object sender, PointerWheelEventArgs e)
    {
        if (InnerTextBox.IsKeyboardFocusWithin
            && TValue.TryParse(InnerTextBox.Text, CultureInfo.CurrentUICulture, out TValue value))
        {
            value = e.Delta.Y switch
            {
                < 0 => value - TValue.CreateTruncating(10),
                > 0 => value + TValue.CreateTruncating(10),
                _ => value
            };

            value = e.Delta.X switch
            {
                < 0 => value - TValue.One,
                > 0 => value + TValue.One,
                _ => value
            };

            Value = value;

            e.Handled = true;
        }
    }
}
