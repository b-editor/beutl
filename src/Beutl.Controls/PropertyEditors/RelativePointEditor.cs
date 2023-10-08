using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Beutl.Controls.PropertyEditors;

public class RelativePointEditor : Vector2Editor
{
    public static readonly DirectProperty<RelativePointEditor, float> FirstValueProperty =
        Vector4Editor<float>.FirstValueProperty.AddOwner<RelativePointEditor>(
            o => o.FirstValue,
            (o, v) => o.FirstValue = v);

    public static readonly DirectProperty<RelativePointEditor, float> SecondValueProperty =
        Vector4Editor<float>.SecondValueProperty.AddOwner<RelativePointEditor>(
            o => o.SecondValue,
            (o, v) => o.SecondValue = v);

    public static readonly DirectProperty<RelativePointEditor, Graphics.RelativeUnit> UnitProperty =
        AvaloniaProperty.RegisterDirect<RelativePointEditor, Graphics.RelativeUnit>(
            nameof(Unit),
            o => o.Unit,
            (o, v) => o.Unit = v,
            defaultBindingMode: BindingMode.TwoWay);

    private float _firstValue;
    private float _oldFirstValue;
    private float _secondValue;
    private float _oldSecondValue;
    private Graphics.RelativeUnit _unit;
    private Graphics.RelativeUnit _oldUnit;

    public float FirstValue
    {
        get => _firstValue;
        set
        {
            if (SetAndRaise(FirstValueProperty, ref _firstValue, value))
            {
                UpdateText();
            }
        }
    }

    public float SecondValue
    {
        get => _secondValue;
        set
        {
            if (SetAndRaise(SecondValueProperty, ref _secondValue, value))
            {
                UpdateText();
            }
        }
    }

    public Graphics.RelativeUnit Unit
    {
        get => _unit;
        set
        {
            if (SetAndRaise(UnitProperty, ref _unit, value))
            {
                UpdateText();
            }
        }
    }

    private void UpdateText()
    {
        if (Unit == Graphics.RelativeUnit.Relative)
        {
            FirstText = $"{_firstValue * 100:f}%";
            SecondText = $"{_secondValue * 100:f}%";
        }
        else
        {
            FirstText = $"{_firstValue}";
            SecondText = $"{_secondValue}";
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        void SubscribeEvents(TextBox textBox)
        {
            textBox.GotFocus += OnInnerTextBoxGotFocus;
            textBox.LostFocus += OnInnerTextBoxLostFocus;
            textBox.GetPropertyChangedObservable(TextBox.TextProperty).Subscribe(e =>
            {
                if (e is AvaloniaPropertyChangedEventArgs<string> args
                    && args.Sender is TextBox textBox)
                {
                    OnInnerTextBoxTextChanged(textBox, args.NewValue.GetValueOrDefault(), args.OldValue.GetValueOrDefault());
                }
            });
            textBox.AddHandler(PointerWheelChangedEvent, OnInnerTextBoxPointerWheelChanged, RoutingStrategies.Tunnel);
        }

        base.OnApplyTemplate(e);
        UpdateText();

        SubscribeEvents(InnerFirstTextBox);
        SubscribeEvents(InnerSecondTextBox);
    }

    private void OnInnerTextBoxGotFocus(object sender, GotFocusEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this))
        {
            _oldFirstValue = FirstValue;
            _oldSecondValue = SecondValue;
            _oldUnit = Unit;
        }
    }

    private void OnInnerTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this))
        {
            if (
                FirstValue != _oldFirstValue
                || SecondValue != _oldSecondValue
                || Unit != _oldUnit)
            {
                RaiseEvent(new PropertyEditorValueChangedEventArgs<Graphics.RelativePoint>(
                    new Graphics.RelativePoint(FirstValue, SecondValue, Unit),
                    new Graphics.RelativePoint(_oldFirstValue, _oldSecondValue, _oldUnit),
                    ValueConfirmedEvent));
            }
        }
    }

    private static bool TryParse(string s, out float result, out Graphics.RelativeUnit unit)
    {
        if (s == null)
        {
            result = default;
            unit = default;
            return false;
        }

        result = 1f;
        float scale = 1f;
        ReadOnlySpan<char> span = s;

        if (s.EndsWith("%", StringComparison.Ordinal))
        {
            scale = 0.01f;
            span = s.AsSpan()[0..^1];
            unit = Graphics.RelativeUnit.Relative;
        }
        else
        {
            unit = Graphics.RelativeUnit.Absolute;
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

    private void OnInnerTextBoxTextChanged(TextBox sender, string newValue, string oldValue)
    {
        if (sender.IsKeyboardFocusWithin
            && TryParse(newValue, out float newValue2, out Graphics.RelativeUnit newUnit))
        {
            bool invalidOldValue = !TryParse(oldValue, out float oldValue2, out Graphics.RelativeUnit oldUnit);
            if (invalidOldValue)
            {
                oldValue2 = newValue2;
                oldUnit = newUnit;
            }

            if (invalidOldValue || newValue2 != oldValue2)
            {
                switch (sender.Name)
                {
                    case "PART_InnerFirstTextBox":
                        FirstValue = newValue2;
                        Unit = newUnit;
                        RaiseEvent(new PropertyEditorValueChangedEventArgs<Graphics.RelativePoint>(
                            new Graphics.RelativePoint(newValue2, SecondValue, newUnit),
                            new Graphics.RelativePoint(oldValue2, SecondValue, oldUnit),
                            ValueChangedEvent));
                        break;
                    case "PART_InnerSecondTextBox":
                        SecondValue = newValue2;
                        Unit = newUnit;
                        RaiseEvent(new PropertyEditorValueChangedEventArgs<Graphics.RelativePoint>(
                            new Graphics.RelativePoint(FirstValue, newValue2, newUnit),
                            new Graphics.RelativePoint(FirstValue, oldValue2, oldUnit),
                            ValueChangedEvent));
                        break;
                    default:
                        break;
                }
            }
        }

        UpdateErrors();
    }

    private void UpdateErrors()
    {
        if (TryParse(InnerFirstTextBox.Text, out _, out _)
            && TryParse(InnerSecondTextBox.Text, out _, out _))
        {
            DataValidationErrors.ClearErrors(this);
        }
        else
        {
            DataValidationErrors.SetErrors(this, DataValidationMessages.InvalidString);
        }
    }

    private void OnInnerTextBoxPointerWheelChanged(object sender, PointerWheelEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this)
            && sender is TextBox textBox
            && textBox.IsKeyboardFocusWithin
            && TryParse(textBox.Text, out float value, out Graphics.RelativeUnit unit))
        {
            float delta1 = 1;
            float delta2 = 10;
            if (unit == Graphics.RelativeUnit.Relative)
            {
                delta1 *= 0.01f;
                delta2 *= 0.01f;
            }

            float delta3 = delta2;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                delta3 = delta1;
            }

            value = e.Delta.Y switch
            {
                < 0 => value - delta3,
                > 0 => value + delta3,
                _ => value
            };

            switch (textBox.Name)
            {
                case "PART_InnerFirstTextBox":
                    FirstValue = value;
                    break;
                case "PART_InnerSecondTextBox":
                    SecondValue = value;
                    break;
                default:
                    break;
            }

            e.Handled = true;
        }
    }
}
