using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

using Beutl.Reactive;

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
                            OnInnerTextBoxTextChanged(textBox, args.NewValue.GetValueOrDefault(), args.OldValue.GetValueOrDefault());
                        }
                    })
                    .DisposeWith(_disposables);
                textBox.AddDisposableHandler(PointerWheelChangedEvent, OnInnerTextBoxPointerWheelChanged, RoutingStrategies.Tunnel)
                    .DisposeWith(_disposables);
            }
        }

        _disposables.Clear();
        base.OnApplyTemplate(e);
        UpdateText();

        SubscribeEvents(InnerFirstTextBox);
        SubscribeEvents(InnerSecondTextBox);

        UpdateErrors();
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
        }

        UpdateErrors();
    }

    private void UpdateErrors()
    {
        if (TryParse(InnerFirstTextBox.Text, out _, out _)
            && (IsUniform || TryParse(InnerSecondTextBox.Text, out _, out _)))
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

            e.Handled = true;
        }
    }
}
