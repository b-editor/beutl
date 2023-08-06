using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Xaml.Interactivity;

using Beutl.Commands;
using Beutl.Controls.Behaviors;
using Beutl.ProjectSystem;
using Beutl.Operation;
using Beutl.ViewModels.Tools;
using Beutl.Services;

namespace Beutl.Views.Tools;

public sealed partial class SourceOperatorView : UserControl
{
    public SourceOperatorView()
    {
        InitializeComponent();
        Interaction.SetBehaviors(this, new BehaviorCollection
        {
            new _DragBehavior()
            {
                Orientation = Orientation.Vertical,
                DragControl = dragBorder
            },
        });
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

    public void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SourceOperatorViewModel viewModel2)
        {
            SourceOperator operation = viewModel2.Model;
            Element layer = operation.FindRequiredHierarchicalParent<Element>();
            layer.Operation.RemoveChild(operation)
                .DoAndRecord(CommandRecorder.Default);
        }
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (e.Data.Get(KnownLibraryItemFormats.SourceOperator) is Type item2
            && DataContext is SourceOperatorViewModel viewModel2)
        {
            SourceOperator operation = viewModel2.Model;
            Element layer = operation.FindRequiredHierarchicalParent<Element>();
            Rect bounds = Bounds;
            Point position = e.GetPosition(this);
            double half = bounds.Height / 2;
            int index = layer.Operation.Children.IndexOf(operation);

            if (half < position.Y)
            {
                layer.Operation.InsertChild(index + 1, (SourceOperator)Activator.CreateInstance(item2)!)
                    .DoAndRecord(CommandRecorder.Default);
            }
            else
            {
                layer.Operation.InsertChild(index, (SourceOperator)Activator.CreateInstance(item2)!)
                    .DoAndRecord(CommandRecorder.Default);
            }

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

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SourceOperatorViewModel viewModel2)
        {
            SourceOperator operation = viewModel2.Model;
            Type type = operation.GetType();
            LibraryItem? item = LibraryService.Current.FindItem(type);

            if (item != null)
            {
                headerText.Text = item.DisplayName;
            }
        }
    }

    private sealed class _DragBehavior : GenericDragBehavior
    {
        protected override void OnMoveDraggedItem(ItemsControl? itemsControl, int oldIndex, int newIndex)
        {
            if (itemsControl?.DataContext is SourceOperatorsTabViewModel { Layer.Value.Operation.Children: { } list })
            {
                list.BeginRecord<SourceOperator>()
                    .Move(oldIndex, newIndex)
                    .ToCommand()
                    .DoAndRecord(CommandRecorder.Default);
            }
        }
    }
}
