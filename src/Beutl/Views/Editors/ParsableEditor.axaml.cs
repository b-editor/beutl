using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class ParsableEditor : UserControl
{
    public ParsableEditor()
    {
        InitializeComponent();
        textBox.LostFocus += TextBox_LostFocus;

        textBox.GetObservable(TextBox.TextProperty).Subscribe(TextBox_TextChanged);
    }

    private void TextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is IParsableEditorViewModel { IsDisposed: false } vm)
        {
            vm.SetValueString(textBox.Text);
        }
    }

    private void TextBox_TextChanged(string? s)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (DataContext is not IParsableEditorViewModel { IsDisposed: false } vm) return;

            await Task.Delay(10);

            vm.SetCurrentValueString(textBox.Text);
        });
    }
}
