using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class ParsableEditor : UserControl
{
    public ParsableEditor()
    {
        InitializeComponent();
    }
}

public sealed class ParsableEditor<T> : ParsableEditor
    where T : IParsable<T>
{
    private T? _oldValue;

    public ParsableEditor()
    {
        textBox.GotFocus += TextBox_GotFocus;
        textBox.LostFocus += TextBox_LostFocus;

        textBox.GetObservable(TextBox.TextProperty).Subscribe(TextBox_TextChanged);
    }

    private void TextBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is not ParsableEditorViewModel<T> vm) return;

        _oldValue = vm.WrappedProperty.GetValue();
    }

    private void TextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ParsableEditorViewModel<T> vm
            && T.TryParse(textBox.Text, CultureInfo.CurrentUICulture, out T? newValue))
        {
            vm.SetValue(_oldValue, newValue);
        }
    }

    private void TextBox_TextChanged(string? s)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (DataContext is not ParsableEditorViewModel<T> vm) return;

            await Task.Delay(10);

            if (T.TryParse(textBox.Text, CultureInfo.CurrentUICulture, out T? newValue))
            {
                vm.WrappedProperty.SetValue(newValue);
            }
        });
    }
}
