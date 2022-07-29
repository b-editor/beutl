using Avalonia.Controls;
using Avalonia.Input;

using BeUtl.ProjectSystem;
using BeUtl.Streaming;
using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public sealed partial class StreamOperatorsTab : UserControl
{
    public StreamOperatorsTab()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is StreamOperatorsTabViewModel viewModel)
        {
            var self = new WeakReference<StreamOperatorsTab>(this);
            viewModel.RequestScroll = obj =>
            {
                if (self.TryGetTarget(out StreamOperatorsTab? @this) && @this.DataContext is StreamOperatorsTabViewModel viewModel)
                {
                    int index = 0;
                    bool found = false;
                    for (; index < viewModel.Items.Count; index++)
                    {
                        StylingOperatorViewModel? item = viewModel.Items[index];
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
        if (e.Data.Get("StreamOperator") is OperatorRegistry.RegistryItem item
            && DataContext is StreamOperatorsTabViewModel vm
            && vm.Layer.Value is Layer layer)
        {
            layer.AddChild((StreamOperator)Activator.CreateInstance(item.Type)!)
                .DoAndRecord(CommandRecorder.Default);

            e.Handled = true;
        }
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains("StreamOperator"))
        {
            e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }
}
