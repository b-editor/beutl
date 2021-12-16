using Avalonia.Controls;
using Avalonia.Input;

using BEditorNext.ProjectSystem;
using BEditorNext.ViewModels.Editors;

namespace BEditorNext.Views.Editors;

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
            SceneLayer layer = vm.Layer;

            layer.AddChild((RenderOperation)Activator.CreateInstance(item.Type)!, CommandRecorder.Default);

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
