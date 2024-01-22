using Avalonia.Controls;
using Avalonia.Input;

using Beutl.Operation;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels.Tools;

using Microsoft.Extensions.DependencyInjection;

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
                        Control? ctrl = @this.itemsControl.ContainerFromIndex(index);
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
        if (e.Data.Get(KnownLibraryItemFormats.SourceOperator) is Type item
            && DataContext is SourceOperatorsTabViewModel vm
            && vm.Element.Value is Element element)
        {
            CommandRecorder recorder = vm.GetRequiredService<CommandRecorder>();
            element.Operation.AddChild((SourceOperator)Activator.CreateInstance(item)!)
                .DoAndRecord(recorder);

            e.Handled = true;
        }
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(KnownLibraryItemFormats.SourceOperator))
        {
            e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }
}
