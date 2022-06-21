using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public partial class BaseVector4Editor : UserControl
{
    public BaseVector4Editor()
    {
        InitializeComponent();
    }
}

public abstract class BaseVector4Editor<T> : BaseVector4Editor
    where T : struct
{
    protected T OldValue;

    protected BaseVector4Editor()
    {
        void AddHandlers(TextBox textBox)
        {
            textBox.GotFocus += TextBox_GotFocus;
            textBox.LostFocus += TextBox_LostFocus;
            textBox.KeyDown += TextBox_KeyDown;
        }

        AddHandlers(xTextBox);
        AddHandlers(yTextBox);
        AddHandlers(zTextBox);
        AddHandlers(wTextBox);

        xTextBox.AddHandler(PointerWheelChangedEvent, XTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);
        yTextBox.AddHandler(PointerWheelChangedEvent, YTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);
        zTextBox.AddHandler(PointerWheelChangedEvent, ZTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);
        wTextBox.AddHandler(PointerWheelChangedEvent, WTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);
    }

    protected abstract bool TryParse(string? x, string? y, string? z, string? w, out T value);

    protected abstract T Clamp(T value);

    protected abstract T IncrementX(T value, int increment);

    protected abstract T IncrementY(T value, int increment);

    protected abstract T IncrementZ(T value, int increment);

    protected abstract T IncrementW(T value, int increment);

    private bool TryParseCore(out T value)
    {
        return TryParse(xTextBox.Text, yTextBox.Text, zTextBox.Text, wTextBox.Text, out value);
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

    private void TextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not BaseEditorViewModel<T> vm) return;

        if (TryParseCore(out T newValue))
        {
            vm.Setter.Value = Clamp(newValue);
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

    private void ZTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        OnPointerWheelChanged(sender, e, IncrementZ);
    }

    private void WTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        OnPointerWheelChanged(sender, e, IncrementW);
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

            vm.Setter.Value = Clamp(value);

            e.Handled = true;
        }
    }
}
