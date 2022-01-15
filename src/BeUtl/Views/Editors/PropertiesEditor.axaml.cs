using Avalonia.Controls;
using Avalonia.Input;

using BeUtl.ProjectSystem;
using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public partial class PropertiesEditor : UserControl
{
    public PropertiesEditor()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (e.Data.Get("RenderOperation") is RenderOperationRegistry.RegistryItem item &&
            DataContext is PropertiesEditorViewModel vm)
        {
            Layer layer = vm.Layer;

            layer.AddChild((LayerOperation)Activator.CreateInstance(item.Type)!, CommandRecorder.Default);

            e.Handled = true;
        }
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains("RenderOperation"))
        {
            e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

}
