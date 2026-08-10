using Avalonia.Controls;

namespace Beutl.Editor.Components.LibraryTab.Views.LibraryViews;

public partial class MaterialsView : UserControl
{
    public MaterialsView()
    {
        InitializeComponent();
        MaterialTreeDragHelper.Attach(MaterialTree);
    }
}
