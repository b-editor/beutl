using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Beutl.Controls.PropertyEditors;

public class PercentageEditor : StringEditor
{
    public static readonly DirectProperty<PercentageEditor, float> ValueProperty =
        NumberEditor<float>.ValueProperty.AddOwner<PercentageEditor>(
            o => o.Value,
            (o, v) => o.Value = v);
    private float _value;
    private float _oldValue;
    private IDisposable _disposable;

    public float Value
    {
        get => _value;
        set
        {
            if (SetAndRaise(ValueProperty, ref _value, value))
            {
                Text = $"{value * 100:f}%";
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
            RaiseEvent(new PropertyEditorValueChangedEventArgs<float>(Value, _oldValue, ValueChangedEvent));
        }
    }

    private static bool TryParse(string s, out float result)
    {
        if (s == null)
        {
            result = default;
            return false;
        }

        result = 1f;
        float scale = 1f;
        ReadOnlySpan<char> span = s;

        if (s.EndsWith("%", StringComparison.Ordinal))
        {
            scale = 0.01f;
            span = s.AsSpan()[0..^1];
        }

        if (float.TryParse(span, out float value))
        {
            result = value * scale;
            return true;
        }
        else
        {
            return false;
        }
    }

    protected override void OnTextBoxTextChanged(string newValue, string oldValue)
    {
        if (TryParse(newValue, out float    newValue2))
        {
            bool invalidOldValue = !TryParse(oldValue, out float oldValue2);
            if (invalidOldValue)
            {
                oldValue2 = newValue2;
            }

            if (invalidOldValue || newValue2 != oldValue2)
            {
                Value = newValue2;
                RaiseEvent(new PropertyEditorValueChangedEventArgs<float>(newValue2, oldValue2, ValueChangingEvent));
            }
        }

        UpdateErrors();
    }

    private void UpdateErrors()
    {
        if (TryParse(InnerTextBox.Text, out _))
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
            && TryParse(InnerTextBox.Text, out float value))
        {
            value = e.Delta.Y switch
            {
                < 0 => value - 0.1f,
                > 0 => value + 0.1f,
                _ => value
            };

            value = e.Delta.X switch
            {
                < 0 => value - 0.01f,
                > 0 => value + 0.01f,
                _ => value
            };

            Value = value;

            e.Handled = true;
        }
    }
}
