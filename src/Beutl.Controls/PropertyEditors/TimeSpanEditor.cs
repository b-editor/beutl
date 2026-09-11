using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Reactive;

namespace Beutl.Controls.PropertyEditors;

public class TimeSpanEditor : StringEditor
{
    public static readonly DirectProperty<TimeSpanEditor, TimeSpan> ValueProperty =
        AvaloniaProperty.RegisterDirect<TimeSpanEditor, TimeSpan>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v,
            defaultBindingMode: BindingMode.TwoWay);

    private TimeSpan _value;
    private TimeSpan _oldValue;
    private readonly CompositeDisposable _disposables = [];

    public TimeSpanEditor()
    {
        Text = TimeSpan.Zero.ToString();
    }

    public TimeSpan Value
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

    protected override Type StyleKeyOverride => typeof(StringEditor);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposables.Clear();
        base.OnApplyTemplate(e);
        InnerTextBox.AddDisposableHandler(PointerWheelChangedEvent, OnTextBoxPointerWheelChanged, RoutingStrategies.Tunnel)
            .DisposeWith(_disposables);
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
            RaiseEvent(new PropertyEditorValueChangedEventArgs<TimeSpan>(Value, _oldValue, ValueConfirmedEvent));
        }
    }

    protected override void OnTextBoxTextChanged(string newValue, string oldValue)
    {
        if (InnerTextBox?.IsKeyboardFocusWithin == true
            && TimeSpan.TryParse(newValue, out TimeSpan newValue2))
        {
            bool invalidOldValue = !TimeSpan.TryParse(oldValue, out TimeSpan oldValue2);
            if (invalidOldValue)
            {
                oldValue2 = newValue2;
            }

            if (invalidOldValue || newValue2 != oldValue2)
            {
                Value = newValue2;
                RaiseEvent(new PropertyEditorValueChangedEventArgs<TimeSpan>(newValue2, oldValue2, ValueChangedEvent));
            }
        }

        UpdateErrors();
    }

    private void UpdateErrors()
    {
        if (TimeSpan.TryParse(InnerTextBox.Text, out _))
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
            && TimeSpan.TryParse(InnerTextBox.Text, out TimeSpan value))
        {
            value = e.Delta.Y switch
            {
                < 0 => value.Subtract(TimeSpan.FromMinutes(1)),
                > 0 => value.Add(TimeSpan.FromMinutes(1)),
                _ => value
            };

            value = e.Delta.X switch
            {
                < 0 => value.Subtract(TimeSpan.FromSeconds(10)),
                > 0 => value.Add(TimeSpan.FromSeconds(10)),
                _ => value
            };

            Value = value;

            e.Handled = true;
        }
    }
}
