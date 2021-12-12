using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using BEditorNext.ViewModels.Editors;

namespace BEditorNext.Views.Editors;

public partial class SizeEditor : UserControl
{
    private Graphics.Size _oldValue;

    public SizeEditor()
    {
        InitializeComponent();
        widthTextBox.GotFocus += TextBox_GotFocus;
        widthTextBox.LostFocus += TextBox_LostFocus;
        widthTextBox.KeyDown += TextBox_KeyDown;
        widthTextBox.AddHandler(PointerWheelChangedEvent, WidthTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);

        heightTextBox.GotFocus += TextBox_GotFocus;
        heightTextBox.LostFocus += TextBox_LostFocus;
        heightTextBox.KeyDown += TextBox_KeyDown;
        heightTextBox.AddHandler(PointerWheelChangedEvent, HeightTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);
    }

    private void TextBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is not SizeEditorViewModel vm) return;
        _oldValue = vm.Setter.Value;
    }

    private void TextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SizeEditorViewModel vm) return;

        if (float.TryParse(widthTextBox.Text, out float newWidth) &&
            float.TryParse(heightTextBox.Text, out float newHeight))
        {
            vm.SetValue(_oldValue, new Graphics.Size(newWidth, newHeight));
        }
    }

    private void TextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SizeEditorViewModel vm) return;

        if (float.TryParse(widthTextBox.Text, out float newWidth) &&
            float.TryParse(heightTextBox.Text, out float newHeight))
        {
            newWidth = Math.Clamp(newWidth, vm.Minimum.Width, vm.Maximum.Width);
            newHeight = Math.Clamp(newHeight, vm.Minimum.Height, vm.Maximum.Height);

            vm.Setter.Value = new Graphics.Size(newWidth, newHeight);
        }
    }

    private void WidthTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not SizeEditorViewModel vm) return;

        if (widthTextBox.IsKeyboardFocusWithin && float.TryParse(widthTextBox.Text, out float value))
        {
            int increment = 10;

            if (e.KeyModifiers == KeyModifiers.Shift)
            {
                increment = 1;
            }

            value += (e.Delta.Y < 0) ? -increment : increment;

            vm.Setter.Value = new Graphics.Size(Math.Clamp(value, vm.Minimum.Width, vm.Maximum.Width), _oldValue.Height);

            e.Handled = true;
        }
    }

    private void HeightTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not SizeEditorViewModel vm) return;

        if (heightTextBox.IsKeyboardFocusWithin && float.TryParse(heightTextBox.Text, out float value))
        {
            int increment = 10;

            if (e.KeyModifiers == KeyModifiers.Shift)
            {
                increment = 1;
            }

            value += (e.Delta.Y < 0) ? -increment : increment;

            vm.Setter.Value = new Graphics.Size(_oldValue.Width, Math.Clamp(value, vm.Minimum.Height, vm.Maximum.Height));

            e.Handled = true;
        }
    }
}
