using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

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
        xTextBox.KeyDown += TextBox_KeyDown;
        xTextBox.AddHandler(PointerWheelChangedEvent, XTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);

        yTextBox.GotFocus += TextBox_GotFocus;
        yTextBox.LostFocus += TextBox_LostFocus;
        yTextBox.KeyDown += TextBox_KeyDown;
        yTextBox.AddHandler(PointerWheelChangedEvent, YTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);
    }

    protected abstract bool TryParse(string? x, string? y, out T value);

    protected abstract T Clamp(T value);

    protected abstract T IncrementX(T value, int increment);

    protected abstract T IncrementY(T value, int increment);

    private void TextBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is not BaseEditorViewModel<T> vm) return;
        OldValue = vm.Setter.Value;
    }

    private void TextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BaseEditorViewModel<T> vm) return;

        if (TryParse(xTextBox.Text, yTextBox.Text, out T newValue))
        {
            vm.SetValue(OldValue, newValue);
        }
    }

    private void TextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not BaseEditorViewModel<T> vm) return;

        if (TryParse(xTextBox.Text, yTextBox.Text, out T newValue))
        {
            vm.Setter.Value = Clamp(newValue);
        }
    }

    private void XTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not BaseEditorViewModel<T> vm) return;

        if (xTextBox.IsKeyboardFocusWithin && TryParse(xTextBox.Text, yTextBox.Text, out T value))
        {
            int increment = 10;

            if (e.KeyModifiers == KeyModifiers.Shift)
            {
                increment = 1;
            }

            value = IncrementX(value, (e.Delta.Y < 0) ? -increment : increment);

            vm.Setter.Value = Clamp(value);

            e.Handled = true;
        }
    }

    private void YTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not BaseEditorViewModel<T> vm) return;

        if (yTextBox.IsKeyboardFocusWithin && TryParse(xTextBox.Text, yTextBox.Text, out T value))
        {
            int increment = 10;

            if (e.KeyModifiers == KeyModifiers.Shift)
            {
                increment = 1;
            }

            value = IncrementY(value, (e.Delta.Y < 0) ? -increment : increment);

            vm.Setter.Value = Clamp(value);

            e.Handled = true;
        }
    }
}
