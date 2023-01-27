using System.Globalization;
using System.Numerics;

using Avalonia;
using Avalonia.Controls;
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
    private IDisposable _disposable;

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
        _disposable?.Dispose();
        base.OnApplyTemplate(e);
        _disposable = InnerTextBox.AddDisposableHandler(PointerWheelChangedEvent, OnTextBoxPointerWheelChanged, RoutingStrategies.Tunnel);
    }

    protected override void OnTextBoxGotFocus(GotFocusEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(InnerTextBox))
        {
            _oldValue = Value;
        }
    }

    protected override void OnTextBoxLostFocus(RoutedEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(InnerTextBox)
            && Value != _oldValue)
        {
            RaiseEvent(new PropertyEditorValueChangedEventArgs<TValue>(Value, _oldValue, ValueChangedEvent));
        }
    }

    protected override void OnTextBoxTextChanged(string newValue, string oldValue)
    {
        if (TValue.TryParse(newValue, CultureInfo.CurrentUICulture, out TValue newValue2))
        {
            bool invalidOldValue = !TValue.TryParse(oldValue, CultureInfo.CurrentUICulture, out TValue oldValue2);
            if (invalidOldValue)
            {
                oldValue2 = newValue2;
            }

            if (invalidOldValue || newValue2 != oldValue2)
            {
                Value = newValue2;
                RaiseEvent(new PropertyEditorValueChangedEventArgs<TValue>(newValue2, oldValue2, ValueChangingEvent));
            }
        }

        UpdateErrors();
    }

    private void UpdateErrors()
    {
        if (TValue.TryParse(InnerTextBox.Text, CultureInfo.CurrentUICulture, out _))
        {
            DataValidationErrors.ClearErrors(InnerTextBox);
        }
        else
        {
            DataValidationErrors.SetErrors(InnerTextBox, DataValidationMessages.InvalidString);
        }
    }

    private void OnTextBoxPointerWheelChanged(object sender, PointerWheelEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(InnerTextBox)
            && InnerTextBox.IsKeyboardFocusWithin
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
