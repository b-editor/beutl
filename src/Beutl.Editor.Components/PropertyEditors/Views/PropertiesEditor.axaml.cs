using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public partial class PropertiesEditor : UserControl
{
    public PropertiesEditor()
    {
        InitializeComponent();
    }

    // The row of the first property, once the rows exist.
    public Control? FirstRow => items.ContainerFromIndex(0);

    // Leaves out the menu of each property editor, as the output settings do.
    public bool HideMenu
    {
        get => ReferenceEquals(items.ItemTemplate, Resources["EditorWithoutMenuTemplate"]);
        set => items.ItemTemplate = (IDataTemplate)Resources[value ? "EditorWithoutMenuTemplate" : "EditorTemplate"]!;
    }
}
