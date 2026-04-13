using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Beutl.Views.Editors;

public partial class PathOperationListItemEditor : UserControl, IListItemEditor
{
    public PathOperationListItemEditor()
    {
        InitializeComponent();
        ExpandTransitionHelper.Attach(reorderHandle, content, ExpandTransitionHelper.ListItemDuration);
    }

    public Control? ReorderHandle => reorderHandle;

    public event EventHandler? DeleteRequested;

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }
}
