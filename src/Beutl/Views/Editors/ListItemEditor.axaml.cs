using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Beutl.Views.Editors;

public partial class ListItemEditor : UserControl
{
    public ListItemEditor()
    {
        InitializeComponent();
    }

    private void Menu_Click(object? sender, RoutedEventArgs e)
    {
        if(sender is Button btn)
        {
            btn.ContextMenu?.Open();
        }
    }

    private void Edit_Click(object? sender, RoutedEventArgs e)
    {
    }

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {

    }
}
