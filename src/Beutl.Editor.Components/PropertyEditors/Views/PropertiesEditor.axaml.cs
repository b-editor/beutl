using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public partial class PropertiesEditor : UserControl
{
    public PropertiesEditor()
    {
        InitializeComponent();
    }

    // What the first property shows, once the rows exist: its row, or a row for each part of a pair kept apart.
    public Control? FirstItem => items.ContainerFromIndex(0);

    // Leaves out the menu of each property editor, as the output settings do.
    public bool HideMenu
    {
        get => ReferenceEquals(items.ItemTemplate, Resources["EditorWithoutMenuTemplate"]);
        set => items.ItemTemplate = (IDataTemplate)Resources[value ? "EditorWithoutMenuTemplate" : "EditorTemplate"]!;
    }
}
