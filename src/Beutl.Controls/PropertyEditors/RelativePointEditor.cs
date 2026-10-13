using System.Reactive.Disposables;
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

    private readonly CompositeDisposable _disposables = [];
    private TextBox? _updatingTextBox;
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
        string Format(float value) => Unit == Graphics.RelativeUnit.Relative ? $"{value * 100:f}%" : $"{value}";

        if (_updatingTextBox == null || _updatingTextBox != InnerFirstTextBox)
        {
            FirstText = Format(FirstValue);
        }
        if (_updatingTextBox == null || _updatingTextBox != InnerSecondTextBox)
        {
            SecondText = Format(SecondValue);
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposables.Clear();
        base.OnApplyTemplate(e);
        UpdateText();

        var valueHandlers = new ComponentValueHandlers(
            OnInnerTextBoxGotFocus, OnInnerTextBoxLostFocus, OnInnerTextBoxTextChanged, OnInnerTextBoxPointerWheelChanged);
        valueHandlers.Subscribe(InnerFirstTextBox, _disposables);
        valueHandlers.Subscribe(InnerSecondTextBox, _disposables);

        UpdateErrors();
    }

    private void OnInnerTextBoxGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this))
        {
            _oldFirstValue = FirstValue;
            _oldSecondValue = SecondValue;
            _oldUnit = Unit;
        }
    }

    private void OnInnerTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this))
        {
            UpdateText();
            if (FirstValue != _oldFirstValue
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

    private static bool TryParse(string? s, out float result, out Graphics.RelativeUnit unit)
        => RelativeUnitParser.TryParse(s, out result, out unit);

    private void OnInnerTextBoxTextChanged(TextBox sender, string? newValue, string? oldValue)
    {
        if (_updatingTextBox == null
            && sender.IsKeyboardFocusWithin
            && RelativeUnitParser.TryParseEdit(
                newValue, oldValue,
                out float newValue2, out Graphics.RelativeUnit newUnit,
                out float oldValue2, out Graphics.RelativeUnit oldUnit))
        {
            // Preserve only the component being typed while bindings coerce the values.
            _updatingTextBox = sender;
            try
            {
                var newValues = (FirstValue, SecondValue);
                var oldValues = (FirstValue, SecondValue);
                Unit = newUnit;
                if (IsUniform)
                {
                    FirstValue = SecondValue = newValue2;
                    newValues = (newValue2, newValue2);
                    oldValues = (oldValue2, oldValue2);
                }
                else
                {
                    switch (sender.Name)
                    {
                        case "PART_InnerFirstTextBox":
                            FirstValue = newValue2;
                            newValues.FirstValue = newValue2;
                            oldValues.FirstValue = oldValue2;
                            break;
                        case "PART_InnerSecondTextBox":
                            SecondValue = newValue2;
                            newValues.SecondValue = newValue2;
                            oldValues.SecondValue = oldValue2;
                            break;
                    }
                }

                RaiseEvent(new PropertyEditorValueChangedEventArgs<Graphics.RelativePoint>(
                    new Graphics.RelativePoint(newValues.FirstValue, newValues.SecondValue, newUnit),
                    new Graphics.RelativePoint(oldValues.FirstValue, oldValues.SecondValue, oldUnit),
                    ValueChangedEvent));
            }
            finally
            {
                _updatingTextBox = null;
            }
        }

        UpdateErrors();
    }

    private void UpdateErrors()
    {
        DataValidationMessages.UpdateInvalidString(
            this,
            TryParse(InnerFirstTextBox.Text, out _, out _)
                && (IsUniform || TryParse(InnerSecondTextBox?.Text, out _, out _)));
    }

    private void OnInnerTextBoxPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this)
            && sender is TextBox textBox
            && textBox.IsKeyboardFocusWithin
            && TryParse(textBox.Text, out float value, out _))
        {
            // The text can still be an out-of-range draft; step from the coerced value.
            value = textBox.Name switch
            {
                "PART_InnerFirstTextBox" => FirstValue,
                "PART_InnerSecondTextBox" => SecondValue,
                _ => value,
            };
            value = RelativeUnitParser.StepByWheel(value, Unit, e);

            if (IsUniform)
            {
                FirstValue = SecondValue = value;
            }
            else
            {
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
            }

            UpdateText();
            e.Handled = true;
        }
    }
}
