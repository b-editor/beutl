using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

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
        xTextBox.KeyDown += TextBox_KeyDown;
        xTextBox.AddHandler(PointerWheelChangedEvent, XTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);

        yTextBox.GotFocus += TextBox_GotFocus;
        yTextBox.LostFocus += TextBox_LostFocus;
        yTextBox.KeyDown += TextBox_KeyDown;
        yTextBox.AddHandler(PointerWheelChangedEvent, YTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);
    }

    protected abstract bool TryParse(string? x, string? y, out T value);

    protected abstract T IncrementX(T value, int increment);

    protected abstract T IncrementY(T value, int increment);

    private bool TryParseCore(out T value)
    {
        return TryParse(xTextBox.Text, yTextBox.Text, out value);
    }

    private void TextBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is not BaseEditorViewModel<T> vm) return;
        OldValue = vm.WrappedProperty.GetValue();
    }

    private void TextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BaseEditorViewModel<T> vm) return;

        if (TryParseCore(out T newValue))
        {
            vm.SetValue(OldValue, newValue);
        }
    }

    private void TextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not BaseEditorViewModel<T> vm) return;

        if (TryParseCore(out T newValue))
        {
            vm.WrappedProperty.SetValue(newValue);
        }
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
            value = e.Delta.Y switch
            {
                < 0 => func(value, -10),
                > 0 => func(value, 10),
                _ => value
            };

            value = e.Delta.X switch
            {
                < 0 => func(value, -1),
                > 0 => func(value, 1),
                _ => value
            };

            vm.WrappedProperty.SetValue(value);

            e.Handled = true;
        }
    }
}
