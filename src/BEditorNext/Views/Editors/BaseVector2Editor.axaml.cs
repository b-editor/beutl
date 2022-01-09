using System.Reactive.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using BEditorNext.ViewModels.Editors;

namespace BEditorNext.Views.Editors;

public partial class BaseVector2Editor : UserControl
{
    public BaseVector2Editor()
    {
        InitializeComponent();
    }
}

public abstract class BaseVector2Editor<T> : BaseVector2Editor
    where T : struct
{
    protected T OldValue;

    protected BaseVector2Editor()
    {
        xTextBox.GotFocus += TextBox_GotFocus;
        xTextBox.LostFocus += TextBox_LostFocus;
        xTextBox.GetObservable(TextBox.TextProperty).Subscribe(TextBox_TextChanged);
        xTextBox.AddHandler(PointerWheelChangedEvent, XTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);

        yTextBox.GotFocus += TextBox_GotFocus;
        yTextBox.LostFocus += TextBox_LostFocus;
        yTextBox.GetObservable(TextBox.TextProperty).Subscribe(TextBox_TextChanged);
        yTextBox.AddHandler(PointerWheelChangedEvent, YTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);
    }

    protected abstract bool TryParse(string? x, string? y, out T value);

    protected abstract T Clamp(T value);

    protected abstract T IncrementX(T value, int increment);

    protected abstract T IncrementY(T value, int increment);

    private bool TryParseCore(out T value)
    {
        bool result = TryParse(xTextBox.Text, yTextBox.Text, out value);
        SetError(!result);
        return result;
    }

    private void TextBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is not BaseEditorViewModel<T> vm) return;
        OldValue = vm.Setter.Value;
    }

    private void TextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BaseEditorViewModel<T> vm) return;

        if (TryParseCore(out T newValue))
        {
            vm.SetValue(OldValue, newValue);
        }
    }

    private void TextBox_TextChanged(string _)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (DataContext is not BaseEditorViewModel<T> vm) return;
            await Task.Delay(10);
            if (TryParseCore(out T newValue))
            {
                vm.Setter.Value = Clamp(newValue);
            }
        });
    }

    private void XTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        OnPointerWheelChanged(sender, e, IncrementX);
    }

    private void YTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        OnPointerWheelChanged(sender, e, IncrementY);
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e, Func<T, int, T> func)
    {
        if (DataContext is not BaseEditorViewModel<T> vm || sender is not TextBox textBox) return;

        if (textBox.IsKeyboardFocusWithin && TryParseCore(out T value))
        {
            int increment = 10;

            if (e.KeyModifiers == KeyModifiers.Shift)
            {
                increment = 1;
            }

            value = func(value, (e.Delta.Y < 0) ? -increment : increment);

            vm.Setter.Value = Clamp(value);

            e.Handled = true;
        }
    }

    private void SetError(bool state)
    {
        (xTextBox.Classes as IPseudoClasses).Set(":error", state);
        (yTextBox.Classes as IPseudoClasses).Set(":error", state);
    }
}
