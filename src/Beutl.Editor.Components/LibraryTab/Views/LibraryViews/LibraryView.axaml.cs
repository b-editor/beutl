using Avalonia.Controls;

namespace Beutl.Editor.Components.LibraryTab.Views.LibraryViews;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
        LibraryTreeDragHelper.Attach(LibraryTree);
    }
}
