using Avalonia.Controls;
using Avalonia.Input;

using BeUtl.ProjectSystem;
using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public sealed partial class OperationsEditor : UserControl
{
    public OperationsEditor()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is OperationsEditorViewModel viewModel)
        {
            var self = new WeakReference<OperationsEditor>(this);
            viewModel.RequestScroll = obj =>
            {
                if (self.TryGetTarget(out OperationsEditor? @this) && @this.DataContext is OperationsEditorViewModel viewModel)
                {
                    int index = 0;
                    bool found = false;
                    for (; index < viewModel.Items.Count; index++)
                    {
                        OperationEditorViewModel? item = viewModel.Items[index];
                        if (ReferenceEquals(item.Model, obj))
                        {
                            found = true;
                            break;
                        }
                    }

                    if (found)
                    {
                        IControl? ctrl = @this.itemsControl.ItemContainerGenerator.ContainerFromIndex(index);
                        if (ctrl != null)
                        {
                            @this.scrollViewer.Offset = new Avalonia.Vector(@this.scrollViewer.Offset.X, ctrl.Bounds.Top);
                        }
                    }
                }
            };
        }
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (e.Data.Get("RenderOperation") is LayerOperationRegistry.RegistryItem item
            && DataContext is OperationsEditorViewModel vm
            && vm.Layer.Value is Layer layer)
        {
            layer.AddChild((LayerOperation)Activator.CreateInstance(item.Type)!)
                .DoAndRecord(CommandRecorder.Default);

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
