using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using BEditorNext.ViewModels.Editors;

namespace BEditorNext.Views.Editors;

public sealed partial class StringEditor : UserControl
{
    private string? _oldValue;

    public StringEditor()
    {
        InitializeComponent();
        textBox.GotFocus += TextBox_GotFocus;
        textBox.LostFocus += TextBox_LostFocus;

        textBox.GetObservable(TextBox.TextProperty).Subscribe(TextBox_TextChanged);
    }

    private void TextBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is not StringEditorViewModel vm) return;

        _oldValue = vm.Setter.Value;
    }

    private void TextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StringEditorViewModel vm) return;

        vm.SetValue(_oldValue, textBox.Text);
    }

    private void TextBox_TextChanged(string s)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (DataContext is not StringEditorViewModel vm) return;

            await Task.Delay(10);

            vm.Setter.Value = textBox.Text;
        });
    }

}
