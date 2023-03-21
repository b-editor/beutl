using Avalonia.Controls;
using Avalonia.Input;

using Beutl.ProjectSystem;
using Beutl.Operation;
using Beutl.ViewModels.Tools;

namespace Beutl.Views.Tools;

public sealed partial class SourceOperatorsTab : UserControl
{
    public SourceOperatorsTab()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SourceOperatorsTabViewModel viewModel)
        {
            var self = new WeakReference<SourceOperatorsTab>(this);
            viewModel.RequestScroll = obj =>
            {
                if (self.TryGetTarget(out SourceOperatorsTab? @this) && @this.DataContext is SourceOperatorsTabViewModel viewModel)
                {
                    int index = 0;
                    bool found = false;
                    for (; index < viewModel.Items.Count; index++)
                    {
                        SourceOperatorViewModel? item = viewModel.Items[index];
                        if (ReferenceEquals(item?.Model, obj))
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
        if (e.Data.Get("SourceOperator") is OperatorRegistry.RegistryItem item
            && DataContext is SourceOperatorsTabViewModel vm
            && vm.Layer.Value is Layer layer)
        {
            layer.Operation.AddChild((SourceOperator)Activator.CreateInstance(item.Type)!)
                .DoAndRecord(CommandRecorder.Default);

            e.Handled = true;
        }
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains("SourceOperator"))
        {
            e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }
}
