using System.Globalization;
using System.Numerics;
using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Reactive;

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

    public static readonly StyledProperty<TValue> LargeChangeProperty =
        AvaloniaProperty.Register<NumberEditor<TValue>, TValue>(
            nameof(LargeChange),
            defaultValue: TValue.CreateTruncating(10));

    public static readonly StyledProperty<TValue> SmallChangeProperty =
        AvaloniaProperty.Register<NumberEditor<TValue>, TValue>(
            nameof(SmallChange),
            defaultValue: TValue.One);

    public static readonly StyledProperty<string?> NumberFormatProperty =
        AvaloniaProperty.Register<NumberEditor<TValue>, string?>(
            nameof(NumberFormat),
            defaultValue: null);

    private TValue _value = TValue.Zero;
    private TValue _oldValue = TValue.Zero;
    private readonly CompositeDisposable _disposables = [];
    private HeaderScrubGesture _scrub;
    private TextBlock? _headerText;

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
                Text = NumberEditorHelper.Format(value, NumberFormat);
            }
        }
    }

    public TValue LargeChange
    {
        get => GetValue(LargeChangeProperty);
        set => SetValue(LargeChangeProperty, value);
    }

    public TValue SmallChange
    {
        get => GetValue(SmallChangeProperty);
        set => SetValue(SmallChangeProperty, value);
    }

    public string? NumberFormat
    {
        get => GetValue(NumberFormatProperty);
        set => SetValue(NumberFormatProperty, value);
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
            TValue delta = _scrub.NextDelta<TValue>(headerText, e) * SmallChange;
            TValue oldValue = Value;
            TValue newValue = NumberEditorHelper.AddPreservingScale(oldValue, delta);
            if (newValue != oldValue)
            {
                Value = newValue;
                RaiseEvent(new PropertyEditorValueChangedEventArgs<TValue>(newValue, oldValue, ValueChangedEvent));
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
                RaiseEvent(new PropertyEditorValueChangedEventArgs<TValue>(Value, _oldValue, ValueConfirmedEvent));
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
            RaiseEvent(new PropertyEditorValueChangedEventArgs<TValue>(Value, _oldValue, ValueConfirmedEvent));
        }
    }

    protected override void OnTextBoxTextChanged(string newValue, string oldValue)
    {
        if (InnerTextBox?.IsKeyboardFocusWithin == true
            && NumberEditorHelper.TryParseEdit(newValue, oldValue, out TValue? newValue2, out TValue? oldValue2))
        {
            Value = newValue2;
            RaiseEvent(new PropertyEditorValueChangedEventArgs<TValue>(newValue2, oldValue2, ValueChangedEvent));
        }

        UpdateErrors();
    }

    private void UpdateErrors()
    {
        if (InnerTextBox == null) return;
        DataValidationMessages.UpdateInvalidString(InnerTextBox, TValue.TryParse(InnerTextBox.Text, CultureInfo.CurrentCulture, out _));
    }

    private void OnTextBoxPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (InnerTextBox == null) return;
        if (!DataValidationErrors.GetHasErrors(InnerTextBox)
            && InnerTextBox.IsKeyboardFocusWithin
            && TValue.TryParse(InnerTextBox.Text, CultureInfo.CurrentCulture, out TValue? value)
            && value is not null)
        {
            value = NumberEditorHelper.StepByWheel(value, e, LargeChange, SmallChange);

            Value = value;

            e.Handled = true;
        }
    }
}
