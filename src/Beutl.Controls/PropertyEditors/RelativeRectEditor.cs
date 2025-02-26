using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Reactive;

namespace Beutl.Controls.PropertyEditors;

public class RelativeRectEditor : Vector4Editor
{
    public static readonly DirectProperty<RelativeRectEditor, float> FirstValueProperty =
        Vector4Editor<float>.FirstValueProperty.AddOwner<RelativeRectEditor>(
            o => o.FirstValue,
            (o, v) => o.FirstValue = v);

    public static readonly DirectProperty<RelativeRectEditor, float> SecondValueProperty =
        Vector4Editor<float>.SecondValueProperty.AddOwner<RelativeRectEditor>(
            o => o.SecondValue,
            (o, v) => o.SecondValue = v);

    public static readonly DirectProperty<RelativeRectEditor, float> ThirdValueProperty =
        Vector4Editor<float>.ThirdValueProperty.AddOwner<RelativeRectEditor>(
            o => o.ThirdValue,
            (o, v) => o.ThirdValue = v);

    public static readonly DirectProperty<RelativeRectEditor, float> FourthValueProperty =
        Vector4Editor<float>.FourthValueProperty.AddOwner<RelativeRectEditor>(
            o => o.FourthValue,
            (o, v) => o.FourthValue = v);

    public static readonly DirectProperty<RelativeRectEditor, Graphics.RelativeUnit> UnitProperty =
        AvaloniaProperty.RegisterDirect<RelativeRectEditor, Graphics.RelativeUnit>(
            nameof(Unit),
            o => o.Unit,
            (o, v) => o.Unit = v,
            defaultBindingMode: BindingMode.TwoWay);

    private readonly CompositeDisposable _disposables = [];
    private float _firstValue;
    private float _oldFirstValue;
    private float _secondValue;
    private float _oldSecondValue;
    private float _thirdValue;
    private float _oldThirdValue;
    private float _fourthValue;
    private float _oldFourthValue;
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

    public float ThirdValue
    {
        get => _thirdValue;
        set
        {
            if (SetAndRaise(ThirdValueProperty, ref _thirdValue, value))
            {
                UpdateText();
            }
        }
    }

    public float FourthValue
    {
        get => _fourthValue;
        set
        {
            if (SetAndRaise(FourthValueProperty, ref _fourthValue, value))
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
            ThirdText = $"{_thirdValue * 100:f}%";
            FourthText = $"{_fourthValue * 100:f}%";
        }
        else
        {
            FirstText = $"{_firstValue}";
            SecondText = $"{_secondValue}";
            ThirdText = $"{_thirdValue}";
            FourthText = $"{_fourthValue}";
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        void SubscribeEvents(TextBox textBox)
        {
            if (textBox != null)
            {
                textBox.AddDisposableHandler(GotFocusEvent, OnInnerTextBoxGotFocus)
                    .DisposeWith(_disposables);
                textBox.AddDisposableHandler(LostFocusEvent, OnInnerTextBoxLostFocus)
                    .DisposeWith(_disposables);
                textBox.GetPropertyChangedObservable(TextBox.TextProperty)
                    .Subscribe(e =>
                    {
                        if (e is AvaloniaPropertyChangedEventArgs<string> args
                            && args.Sender is TextBox textBox)
                        {
                            OnInnerTextBoxTextChanged(textBox, args.NewValue.GetValueOrDefault(),
                                args.OldValue.GetValueOrDefault());
                        }
                    })
                    .DisposeWith(_disposables);
                textBox.AddDisposableHandler(PointerWheelChangedEvent, OnInnerTextBoxPointerWheelChanged,
                        RoutingStrategies.Tunnel)
                    .DisposeWith(_disposables);
            }
        }

        _disposables.Clear();
        base.OnApplyTemplate(e);
        UpdateText();

        SubscribeEvents(InnerFirstTextBox);
        SubscribeEvents(InnerSecondTextBox);
        SubscribeEvents(InnerThirdTextBox);
        SubscribeEvents(InnerFourthTextBox);

        UpdateErrors();
    }

    private void OnInnerTextBoxGotFocus(object sender, GotFocusEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this))
        {
            _oldFirstValue = FirstValue;
            _oldSecondValue = SecondValue;
            _oldThirdValue = ThirdValue;
            _oldFourthValue = FourthValue;
            _oldUnit = Unit;
        }
    }

    private void OnInnerTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this))
        {
            if (FirstValue != _oldFirstValue
                || SecondValue != _oldSecondValue
                || ThirdValue != _oldThirdValue
                || FourthValue != _oldFourthValue
                || Unit != _oldUnit)
            {
                RaiseEvent(new PropertyEditorValueChangedEventArgs<Graphics.RelativeRect>(
                    new Graphics.RelativeRect(FirstValue, SecondValue, ThirdValue, FourthValue, Unit),
                    new Graphics.RelativeRect(_oldFirstValue, _oldSecondValue, _oldThirdValue, _oldFourthValue,
                        _oldUnit),
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

        if (s.EndsWith('%'))
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
                var newValues = (FirstValue, SecondValue, ThirdValue, FourthValue);
                var oldValues = (FirstValue, SecondValue, ThirdValue, FourthValue);
                Unit = newUnit;
                if (IsUniform)
                {
                    FirstValue = SecondValue = ThirdValue = FourthValue = newValue2;
                    newValues = (newValue2, newValue2, newValue2, newValue2);
                    oldValues = (oldValue2, oldValue2, oldValue2, oldValue2);
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
                        case "PART_InnerThirdTextBox":
                            ThirdValue = newValue2;
                            newValues.ThirdValue = newValue2;
                            oldValues.ThirdValue = oldValue2;
                            break;
                        case "PART_InnerFourthTextBox":
                            FourthValue = newValue2;
                            newValues.FourthValue = newValue2;
                            oldValues.FourthValue = oldValue2;
                            break;
                    }
                }

                RaiseEvent(new PropertyEditorValueChangedEventArgs<Graphics.RelativeRect>(
                    new Graphics.RelativeRect(newValues.FirstValue, newValues.SecondValue, newValues.ThirdValue, newValues.FourthValue, newUnit),
                    new Graphics.RelativeRect(oldValues.FirstValue, oldValues.SecondValue, oldValues.ThirdValue, oldValues.FourthValue, oldUnit),
                    ValueChangedEvent));
            }
        }

        UpdateErrors();
    }

    private void UpdateErrors()
    {
        if (TryParse(InnerFirstTextBox.Text, out _, out _)
            && (IsUniform || TryParse(InnerSecondTextBox.Text, out _, out _) || TryParse(InnerThirdTextBox.Text, out _, out _) || TryParse(InnerFourthTextBox.Text, out _, out _)))
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

            if (IsUniform)
            {
                FirstValue = SecondValue = ThirdValue = FourthValue = value;
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
                    case "PART_InnerThirdTextBox":
                        ThirdValue = value;
                        break;
                    case "PART_InnerFourthTextBox":
                        FourthValue = value;
                        break;
                    default:
                        break;
                }
            }

            e.Handled = true;
        }
    }
}
