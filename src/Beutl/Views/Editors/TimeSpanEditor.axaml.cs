using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public sealed partial class TimeSpanEditor : UserControl
{
    private TimeSpan _oldValue;

    public TimeSpanEditor()
    {
        InitializeComponent();

        textBox.GotFocus += TextBox_GotFocus;
        textBox.LostFocus += TextBox_LostFocus;
        textBox.AddHandler(PointerWheelChangedEvent, TextBox_PointerWheelChanged, RoutingStrategies.Tunnel);

        textBox.GetObservable(TextBox.TextProperty).Subscribe(TextBox_TextChanged);
    }

    private void TextBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is not TimeSpanEditorViewModel vm) return;

        _oldValue = vm.PropertyAdapter.GetValue();
    }

    private void TextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TimeSpanEditorViewModel { PropertyAdapter: { } property } viewModel
            && TimeSpan.TryParse(textBox.Text, out TimeSpan newValue))
        {
            viewModel.SetValue(_oldValue, newValue);
        }
    }

    private void TextBox_TextChanged(string? s)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (DataContext is TimeSpanEditorViewModel { PropertyAdapter: { } property } viewModel)
            {
                await Task.Delay(10);

                if (TimeSpan.TryParse(textBox.Text, out TimeSpan value))
                {
                    property.SetValue(value);
                }
            }
        });
    }

    private void TextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is TimeSpanEditorViewModel { PropertyAdapter: { } property } viewModel
            && textBox.IsKeyboardFocusWithin
            && TimeSpan.TryParse(textBox.Text, out TimeSpan value))
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

            property.SetValue(value);

            e.Handled = true;
        }
    }
}
