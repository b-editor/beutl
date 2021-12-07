using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace BEditorNext.Views;

public sealed partial class LayerHeader : UserControl
{
    public LayerHeader()
    {
        InitializeComponent();
        NameTextBox.AddHandler(KeyDownEvent, NameTextBox_KeyDown, RoutingStrategies.Tunnel);
    }

    private void NameTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
        {
            Application.Current.FocusManager.Focus(null);
        }
    }
}
