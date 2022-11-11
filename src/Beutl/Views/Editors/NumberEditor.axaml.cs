using System.Numerics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class NumberEditor : UserControl
{
    public NumberEditor()
    {
        InitializeComponent();
    }
}

public sealed class NumberEditor<T> : NumberEditor
    where T : struct, INumber<T>
{
    private T _oldValue;

    public NumberEditor()
    {
        textBox.GotFocus += TextBox_GotFocus;
        textBox.LostFocus += TextBox_LostFocus;
        textBox.AddHandler(PointerWheelChangedEvent, TextBox_PointerWheelChanged, RoutingStrategies.Tunnel);

        textBox.GetObservable(TextBox.TextProperty).Subscribe(TextBox_TextChanged);
    }

    private void TextBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is not NumberEditorViewModel<T> vm) return;

        _oldValue = vm.WrappedProperty.GetValue();
    }

    private void TextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NumberEditorViewModel<T> { WrappedProperty: { } property } viewModel
            && viewModel.TryParse(textBox.Text, out T newValue))
        {
            viewModel.SetValue(_oldValue, newValue);
        }
    }

    private void TextBox_TextChanged(string? s)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (DataContext is NumberEditorViewModel<T> { WrappedProperty: { } property } viewModel)
            {
                await Task.Delay(10);

                if (viewModel.TryParse(textBox.Text, out T value))
                {
                    property.SetValue(value);
                }
            }
        });
    }

    private void TextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is NumberEditorViewModel<T> { WrappedProperty: { } property } viewModel
            && textBox.IsKeyboardFocusWithin
            && viewModel.TryParse(textBox.Text, out T value))
        {
            value = e.Delta.Y switch
            {
                < 0 => viewModel.Decrement(value, 10),
                > 0 => viewModel.Increment(value, 10),
                _ => value
            };

            value = e.Delta.X switch
            {
                < 0 => viewModel.Decrement(value, 1),
                > 0 => viewModel.Increment(value, 1),
                _ => value
            };

            property.SetValue(value);

            e.Handled = true;
        }
    }
}
