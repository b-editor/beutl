using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public sealed class PercentageEditor : NumberEditor
{
    private float _oldValue;

    public PercentageEditor()
    {
        textBox.GotFocus += TextBox_GotFocus;
        textBox.LostFocus += TextBox_LostFocus;
        textBox.AddHandler(PointerWheelChangedEvent, TextBox_PointerWheelChanged, RoutingStrategies.Tunnel);

        textBox.GetObservable(TextBox.TextProperty).Subscribe(TextBox_TextChanged);
    }

    private void TextBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is not PercentageEditorViewModel vm) return;

        _oldValue = vm.WrappedProperty.GetValue();
    }

    private void TextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PercentageEditorViewModel { WrappedProperty: { } property } viewModel
            && TryParse(textBox.Text, out float newValue))
        {
            viewModel.SetValue(_oldValue, newValue);
        }
    }

    private void TextBox_TextChanged(string? s)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (DataContext is PercentageEditorViewModel { WrappedProperty: { } property })
            {
                await Task.Delay(10);

                if (TryParse(textBox.Text, out float value))
                {
                    property.SetValue(value);
                }
            }
        });
    }

    private void TextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is PercentageEditorViewModel { WrappedProperty: { } property }
            && textBox.IsKeyboardFocusWithin
            && TryParse(textBox.Text, out float value))
        {
            value = e.Delta.Y switch
            {
                < 0 => value - 0.1f,
                > 0 => value + 0.1f,
                _ => value
            };

            value = e.Delta.X switch
            {
                < 0 => value - 0.01f,
                > 0 => value + 0.01f,
                _ => value
            };

            property.SetValue(value);

            e.Handled = true;
        }
    }

    private static bool TryParse(string? s, out float result)
    {
        if (s == null)
        {
            result = default;
            return false;
        }

        result = 1f;
        float scale = 1f;
        ReadOnlySpan<char> span = s;

        if (s.EndsWith("%", StringComparison.Ordinal))
        {
            scale = 0.01f;
            span = s[0..^1];
        }

        if (float.TryParse(span, out float value))
        {
            result = value * scale;
            return true;
        }
        else
        {
            return false;
        }
    }
}
