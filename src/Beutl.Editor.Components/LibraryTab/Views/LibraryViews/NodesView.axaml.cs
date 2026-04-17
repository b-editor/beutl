using Avalonia.Controls;

namespace Beutl.Editor.Components.LibraryTab.Views.LibraryViews;

public partial class NodesView : UserControl
{
    public NodesView()
    {
        InitializeComponent();
        LibraryTreeDragHelper.Attach(NodeTreeView);
    }
}
