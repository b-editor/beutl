using Avalonia.Controls;
using Avalonia.Input;
using Beutl.Editor;
using Beutl.Editor.Components.SourceOperatorsTab.ViewModels;
using Beutl.Operation;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.SourceOperatorsTab.Views;

public sealed partial class SourceOperatorsTabView : UserControl
{
    public SourceOperatorsTabView()
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
            var self = new WeakReference<SourceOperatorsTabView>(this);
            viewModel.RequestScroll = obj =>
            {
                if (self.TryGetTarget(out SourceOperatorsTabView? @this) && @this.DataContext is SourceOperatorsTabViewModel viewModel)
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
        if (e.DataTransfer.TryGetValue(BeutlDataFormats.SourceOperator) is { } typeName
            && TypeFormat.ToType(typeName) is { } item
            && DataContext is SourceOperatorsTabViewModel vm
            && vm.Element.Value is Element element)
        {
            HistoryManager history = vm.GetRequiredService<HistoryManager>();
            element.Operation.AddChild((SourceOperator)Activator.CreateInstance(item)!);
            history.Commit(CommandNames.AddSourceOperator);

            e.Handled = true;
        }
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(BeutlDataFormats.SourceOperator))
        {
            e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
        }
    }
}
