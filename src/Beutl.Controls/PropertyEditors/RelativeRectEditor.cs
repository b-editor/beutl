using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Language;

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
    private Control? _editSource;
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

    public RelativeRectEditor()
    {
        ThirdHeader = Strings.Width;
        FourthHeader = Strings.Height;
    }

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
        string Format(float value) => Unit == Graphics.RelativeUnit.Relative ? $"{value * 100:f}%" : $"{value}";

        if (_editSource == null || _editSource != InnerFirstTextBox)
        {
            FirstText = Format(FirstValue);
        }
        if (_editSource == null || _editSource != InnerSecondTextBox)
        {
            SecondText = Format(SecondValue);
        }
        if (_editSource == null || _editSource != InnerThirdTextBox)
        {
            ThirdText = Format(ThirdValue);
        }
        if (_editSource == null || _editSource != InnerFourthTextBox)
        {
            FourthText = Format(FourthValue);
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
        valueHandlers.Subscribe(InnerThirdTextBox, _disposables);
        valueHandlers.Subscribe(InnerFourthTextBox, _disposables);

        UpdateErrors();
    }

    private void OnInnerTextBoxGotFocus(object? sender, FocusChangedEventArgs e)
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

    private void OnInnerTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this))
        {
            UpdateText();
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

    private static bool TryParse(string? s, out float result, out Graphics.RelativeUnit unit)
        => RelativeUnitParser.TryParse(s, out result, out unit);

    private void OnInnerTextBoxTextChanged(TextBox sender, string? newValue, string? oldValue)
    {
        if (_editSource == null
            && sender.IsKeyboardFocusWithin
            && RelativeUnitParser.TryParseEdit(
                newValue, oldValue, out float newValue2, out Graphics.RelativeUnit newUnit,
                out float oldValue2, out Graphics.RelativeUnit oldUnit))
        {
            using var edit = new PropertyEditorEditScope(ref _editSource, sender);
            ApplyValueChange(sender, newValue2, oldValue2, newUnit, oldUnit);
        }

        UpdateErrors();
    }

    private void ApplyValueChange(TextBox sender, float value, float oldValue,
        Graphics.RelativeUnit unit, Graphics.RelativeUnit oldUnit)
    {
        bool first = IsUniform || sender == InnerFirstTextBox;
        bool second = IsUniform || sender == InnerSecondTextBox;
        bool third = IsUniform || sender == InnerThirdTextBox;
        bool fourth = IsUniform || sender == InnerFourthTextBox;
        var newValues = (first ? value : FirstValue, second ? value : SecondValue,
            third ? value : ThirdValue, fourth ? value : FourthValue);
        var oldValues = _editSource == sender
            ? (first ? oldValue : FirstValue, second ? oldValue : SecondValue,
                third ? oldValue : ThirdValue, fourth ? oldValue : FourthValue)
            : (FirstValue, SecondValue, ThirdValue, FourthValue);
        Unit = unit;
        (FirstValue, SecondValue, ThirdValue, FourthValue) = newValues;
        RaiseEvent(new PropertyEditorValueChangedEventArgs<Graphics.RelativeRect>(
            new Graphics.RelativeRect(newValues.Item1, newValues.Item2, newValues.Item3, newValues.Item4, unit),
            new Graphics.RelativeRect(oldValues.Item1, oldValues.Item2, oldValues.Item3, oldValues.Item4, oldUnit),
            ValueChangedEvent));
    }

    private void UpdateErrors()
    {
        DataValidationMessages.UpdateInvalidString(
            this,
            TryParse(InnerFirstTextBox.Text, out _, out _)
                && (IsUniform
                    || (TryParse(InnerSecondTextBox?.Text, out _, out _)
                        && TryParse(InnerThirdTextBox?.Text, out _, out _)
                        && TryParse(InnerFourthTextBox?.Text, out _, out _))));
    }

    private void OnInnerTextBoxPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(this)
            && sender is TextBox textBox
            && textBox.IsKeyboardFocusWithin
            && TryParse(textBox.Text, out float value, out _))
        {
            using var edit = new PropertyEditorEditScope(ref _editSource, this);
            float oldValue = textBox.Name switch
            {
                "PART_InnerFirstTextBox" => FirstValue,
                "PART_InnerSecondTextBox" => SecondValue,
                "PART_InnerThirdTextBox" => ThirdValue,
                "PART_InnerFourthTextBox" => FourthValue,
                _ => value,
            };
            value = RelativeUnitParser.StepByWheel(oldValue, Unit, e);
            if (value != oldValue)
                ApplyValueChange(textBox, value, oldValue, Unit, Unit);
            UpdateText();
            e.Handled = true;
        }
    }
}
