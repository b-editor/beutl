using System.Globalization;
using System.Numerics;
using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

using Beutl.Configuration;
using Beutl.Reactive;

namespace Beutl.Controls.PropertyEditors;
#pragma warning disable AVP1002 // AvaloniaProperty objects should not be owned by a generic type

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
    private readonly CompositeDisposable _disposables = [];
    private bool _headerPressed;
    private Point _headerDragStart;
    private TextBlock _headerText;

    public NumberEditor()
    {
        Text = "0";
    }

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
        _disposables.Clear();
        base.OnApplyTemplate(e);
        InnerTextBox.AddDisposableHandler(PointerWheelChangedEvent, OnTextBoxPointerWheelChanged, RoutingStrategies.Tunnel)
            .DisposeWith(_disposables);

        _headerText = e.NameScope.Find<TextBlock>("PART_HeaderTextBlock");
        if (_headerText != null)
        {
            _headerText.AddDisposableHandler(PointerPressedEvent, OnTextBlockPointerPressed)
                .DisposeWith(_disposables);
            _headerText.AddDisposableHandler(PointerReleasedEvent, OnTextBlockPointerReleased)
                .DisposeWith(_disposables);
            _headerText.AddDisposableHandler(PointerMovedEvent, OnTextBlockPointerMoved)
                .DisposeWith(_disposables);
            _headerText.Cursor = PointerLockHelper.SizeWestEast;
        }
    }

    private void OnTextBlockPointerMoved(object sender, PointerEventArgs e)
    {
        if (!InnerTextBox.IsKeyboardFocusWithin && _headerPressed)
        {
            Point point = e.GetPosition(_headerText);

            // 値を更新
            Point move = point - _headerDragStart;
            TValue delta = TValue.CreateTruncating(move.X);
            TValue oldValue = Value;
            TValue newValue = Value + delta;
            if (newValue != oldValue)
            {
                Value = newValue;
                RaiseEvent(new PropertyEditorValueChangedEventArgs<TValue>(newValue, oldValue, ValueChangedEvent));
            }

            // ポインタロック
            PointerLockHelper.Moved(_headerText, point, ref _headerDragStart);

            e.Handled = true;

            UpdateErrors();
        }
    }

    private void OnTextBlockPointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (_headerPressed)
        {
            if (Value != _oldValue)
            {
                RaiseEvent(new PropertyEditorValueChangedEventArgs<TValue>(Value, _oldValue, ValueConfirmedEvent));
            }

            PointerLockHelper.Released();

            _headerPressed = false;
            e.Handled = true;
        }
    }

    private void OnTextBlockPointerPressed(object sender, PointerPressedEventArgs e)
    {
        PointerPoint pointerPoint = e.GetCurrentPoint(_headerText);
        if (pointerPoint.Properties.IsLeftButtonPressed
            && !DataValidationErrors.GetHasErrors(InnerTextBox))
        {
            _oldValue = Value;
            PointerLockHelper.Pressed();

            _headerDragStart = pointerPoint.Position;
            _headerPressed = true;
            e.Handled = true;
        }
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
            RaiseEvent(new PropertyEditorValueChangedEventArgs<TValue>(Value, _oldValue, ValueConfirmedEvent));
        }
    }

    protected override void OnTextBoxTextChanged(string newValue, string oldValue)
    {
        if (InnerTextBox?.IsKeyboardFocusWithin == true
            && TValue.TryParse(newValue, CultureInfo.CurrentUICulture, out TValue newValue2))
        {
            bool invalidOldValue = !TValue.TryParse(oldValue, CultureInfo.CurrentUICulture, out TValue oldValue2);
            if (invalidOldValue)
            {
                oldValue2 = newValue2;
            }

            if (invalidOldValue || newValue2 != oldValue2)
            {
                Value = newValue2;
                RaiseEvent(new PropertyEditorValueChangedEventArgs<TValue>(newValue2, oldValue2, ValueChangedEvent));
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
            TValue delta = TValue.CreateTruncating(10);
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                delta = TValue.One;
            }

            value = e.Delta.Y switch
            {
                < 0 => value - delta,
                > 0 => value + delta,
                _ => value
            };

            Value = value;

            e.Handled = true;
        }
    }
}
