using System.Globalization;
using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Reactive;

namespace Beutl.Controls.PropertyEditors;

public class RationalEditor : StringEditor
{
    public static readonly DirectProperty<RationalEditor, Rational> ValueProperty =
        AvaloniaProperty.RegisterDirect<RationalEditor, Rational>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v,
            defaultBindingMode: BindingMode.TwoWay);
    private Rational _value;
    private Rational _oldValue;
    private readonly CompositeDisposable _disposables = [];
    private bool _headerPressed;
    private Point _headerDragStart;
    private TextBlock _headerText;

    public RationalEditor()
    {
        Text = "0/1";
    }

    public Rational Value
    {
        get => _value;
        set
        {
            if (SetAndRaise(ValueProperty, ref _value, value))
            {
                Text = $"{value.Numerator}/{value.Denominator}";
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

            // ポインタロック + デルタ取得
            Point move = PointerLockHelper.Moved(_headerText, point, ref _headerDragStart);
            var delta = new Rational((int)move.X, 1);
            Rational oldValue = Value;
            Rational newValue = Value + delta;
            if (newValue != oldValue)
            {
                Value = newValue;
                RaiseEvent(new PropertyEditorValueChangedEventArgs<Rational>(newValue, oldValue, ValueChangedEvent));
            }

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
                RaiseEvent(new PropertyEditorValueChangedEventArgs<Rational>(Value, _oldValue, ValueConfirmedEvent));
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
            _headerDragStart = pointerPoint.Position;
            PointerLockHelper.Pressed(_headerText, _headerDragStart);
            _headerPressed = true;
            e.Handled = true;
        }
    }

    protected override void OnTextBoxGotFocus(FocusChangedEventArgs e)
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
            RaiseEvent(new PropertyEditorValueChangedEventArgs<Rational>(Value, _oldValue, ValueConfirmedEvent));
        }
    }

    protected override void OnTextBoxTextChanged(string newValue, string oldValue)
    {
        if (InnerTextBox?.IsKeyboardFocusWithin == true
            && Rational.TryParse(newValue, CultureInfo.CurrentUICulture, out Rational newValue2))
        {
            bool invalidOldValue = !Rational.TryParse(oldValue, CultureInfo.CurrentUICulture, out Rational oldValue2);
            if (invalidOldValue)
            {
                oldValue2 = newValue2;
            }

            if (invalidOldValue || newValue2 != oldValue2)
            {
                Value = newValue2;
                RaiseEvent(new PropertyEditorValueChangedEventArgs<Rational>(newValue2, oldValue2, ValueChangedEvent));
            }
        }

        UpdateErrors();
    }

    private void UpdateErrors()
    {
        if (Rational.TryParse(InnerTextBox.Text, CultureInfo.CurrentUICulture, out _))
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
            && Rational.TryParse(InnerTextBox.Text, CultureInfo.CurrentUICulture, out Rational value))
        {
            var delta = new Rational(10);
            double wheelDelta = e.Delta.Y;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                delta = Rational.One;
                wheelDelta = -e.Delta.X;
            }

            value = wheelDelta switch
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
