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
    private HeaderScrubGesture _scrub;
    private TextBlock? _headerText;

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
        if (InnerTextBox == null) return;
        InnerTextBox.AddDisposableHandler(PointerWheelChangedEvent, OnTextBoxPointerWheelChanged, RoutingStrategies.Tunnel)
            .DisposeWith(_disposables);

        _headerText = e.NameScope.Find<TextBlock>("PART_HeaderTextBlock");
        new ScrubHeaderHandlers(
                OnTextBlockPointerPressed, OnTextBlockPointerReleased, OnTextBlockPointerMoved,
                RoutingStrategies.Direct | RoutingStrategies.Bubble)
            .Subscribe(_headerText, _disposables);
    }

    private void OnTextBlockPointerMoved(object? sender, PointerEventArgs e)
    {
        if (InnerTextBox == null) return;
        if (_headerText is not { } headerText) return;
        if (!InnerTextBox.IsKeyboardFocusWithin && _scrub.IsActive)
        {
            int truncated = _scrub.NextDelta<int>(headerText, e);
            var delta = new Rational(truncated, 1);
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

    private void OnTextBlockPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_scrub.IsActive)
        {
            if (Value != _oldValue)
            {
                RaiseEvent(new PropertyEditorValueChangedEventArgs<Rational>(Value, _oldValue, ValueConfirmedEvent));
            }

            _scrub.End();
            e.Handled = true;
        }
    }

    private void OnTextBlockPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (InnerTextBox == null) return;
        if (_headerText is not { } headerText) return;
        PointerPoint pointerPoint = e.GetCurrentPoint(headerText);
        if (pointerPoint.Properties.IsLeftButtonPressed
            && !DataValidationErrors.GetHasErrors(InnerTextBox))
        {
            _oldValue = Value;
            _scrub.Begin(headerText, pointerPoint.Position);
            e.Handled = true;
        }
    }

    protected override void OnTextBoxGotFocus(FocusChangedEventArgs e)
    {
        if (InnerTextBox == null) return;
        if (!DataValidationErrors.GetHasErrors(InnerTextBox))
        {
            _oldValue = Value;
        }
    }

    protected override void OnTextBoxLostFocus(RoutedEventArgs e)
    {
        if (InnerTextBox == null) return;
        if (!DataValidationErrors.GetHasErrors(InnerTextBox)
            && Value != _oldValue)
        {
            RaiseEvent(new PropertyEditorValueChangedEventArgs<Rational>(Value, _oldValue, ValueConfirmedEvent));
        }
    }

    protected override void OnTextBoxTextChanged(string newValue, string oldValue)
    {
        if (InnerTextBox?.IsKeyboardFocusWithin == true
            && NumberEditorHelper.TryParseEdit(newValue, oldValue, out Rational newValue2, out Rational oldValue2))
        {
            Value = newValue2;
            RaiseEvent(new PropertyEditorValueChangedEventArgs<Rational>(newValue2, oldValue2, ValueChangedEvent));
        }

        UpdateErrors();
    }

    private void UpdateErrors()
    {
        if (InnerTextBox == null) return;
        DataValidationMessages.UpdateInvalidString(InnerTextBox, Rational.TryParse(InnerTextBox.Text, CultureInfo.CurrentCulture, out _));
    }

    private void OnTextBoxPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (InnerTextBox == null) return;
        if (!DataValidationErrors.GetHasErrors(InnerTextBox)
            && InnerTextBox.IsKeyboardFocusWithin
            && Rational.TryParse(InnerTextBox.Text, CultureInfo.CurrentCulture, out Rational value))
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
