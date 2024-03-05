using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Beutl.Views.Editors;

public partial class ListItemEditorHost : UserControl, IListItemEditor
{
    public ListItemEditorHost()
    {
        InitializeComponent();
    }

    public Control? ReorderHandle => reorderHandle;

    public event EventHandler? DeleteRequested;

    public void SetChild(Control control)
    {
        content.Content = control;
    }

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }
}
