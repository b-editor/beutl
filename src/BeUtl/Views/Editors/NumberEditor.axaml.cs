using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using BeUtl.Services.Editors;
using BeUtl.Services.Editors.Wrappers;
using BeUtl.Streaming;
using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public partial class NumberEditor : UserControl
{
    public NumberEditor()
    {
        InitializeComponent();
    }
}

public sealed class NumberEditor<T> : NumberEditor
    where T : struct
{
    private T _oldValue;

    public NumberEditor()
    {
        textBox.GotFocus += TextBox_GotFocus;
        textBox.LostFocus += TextBox_LostFocus;
        textBox.AddHandler(PointerWheelChangedEvent, TextBox_PointerWheelChanged, RoutingStrategies.Tunnel);

        textBox.GetObservable(TextBox.TextProperty).Subscribe(TextBox_TextChanged);
    }

    private bool TryParse(INumberEditorService<T> service, IWrappedProperty<T> property, out T result)
    {
        bool parsed;
        if (property is SetterDescription<T>.InternalSetter { Description.Parser: { } parser })
        {
            (result, parsed) = parser(textBox.Text);
            return parsed;
        }
        else
        {
            return service.TryParse(textBox.Text, out result);
        }
    }

    private void TextBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is not NumberEditorViewModel<T> vm) return;

        _oldValue = vm.WrappedProperty.GetValue();
    }

    private void TextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NumberEditorViewModel<T> { EditorService: { } service, WrappedProperty: { } property } viewModel
            && TryParse(service, property, out T newValue))
        {
            viewModel.SetValue(_oldValue, newValue);
        }
    }

    private void TextBox_TextChanged(string s)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (DataContext is NumberEditorViewModel<T> { EditorService: { } service, WrappedProperty: { } property })
            {
                await Task.Delay(10);

                if (TryParse(service, property, out T value))
                {
                    property.SetValue(service.Clamp(value, service.GetMinimum(property), service.GetMaximum(property)));
                }
            }
        });
    }

    private void TextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is NumberEditorViewModel<T> { EditorService: { } service, WrappedProperty: { } property }
            && textBox.IsKeyboardFocusWithin
            && TryParse(service, property, out T value))
        {
            value = e.Delta.Y switch
            {
                < 0 => service.Decrement(value, 10),
                > 0 => service.Increment(value, 10),
                _ => value
            };

            value = e.Delta.X switch
            {
                < 0 => service.Decrement(value, 1),
                > 0 => service.Increment(value, 1),
                _ => value
            };

            property.SetValue(service.Clamp(value, service.GetMinimum(property), service.GetMaximum(property)));

            e.Handled = true;
        }
    }
}
