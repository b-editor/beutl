using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Xaml.Interactivity;
using Beutl.Controls.Behaviors;
using Beutl.Operation;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Views.Tools;

public sealed partial class SourceOperatorView : UserControl
{
    public SourceOperatorView()
    {
        InitializeComponent();
        Interaction.SetBehaviors(this,
        [
            new _DragBehavior() { Orientation = Orientation.Vertical, DragControl = dragBorder },
        ]);
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

    public void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SourceOperatorViewModel viewModel2)
        {
            CommandRecorder recorder = viewModel2.GetRequiredService<CommandRecorder>();
            SourceOperator operation = viewModel2.Model;
            Element element = operation.FindRequiredHierarchicalParent<Element>();
            element.Operation.RemoveChild(operation)
                .DoAndRecord(recorder);
        }
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(KnownLibraryItemFormats.SourceOperator)
            && e.Data.Get(KnownLibraryItemFormats.SourceOperator) is Type item2
            && DataContext is SourceOperatorViewModel viewModel2)
        {
            CommandRecorder recorder = viewModel2.GetRequiredService<CommandRecorder>();
            SourceOperator operation = viewModel2.Model;
            Element element = operation.FindRequiredHierarchicalParent<Element>();
            Rect bounds = Bounds;
            Point position = e.GetPosition(this);
            double half = bounds.Height / 2;
            int index = element.Operation.Children.IndexOf(operation);

            if (half < position.Y)
            {
                element.Operation.InsertChild(index + 1, (SourceOperator)Activator.CreateInstance(item2)!)
                    .DoAndRecord(recorder);
            }
            else
            {
                element.Operation.InsertChild(index, (SourceOperator)Activator.CreateInstance(item2)!)
                    .DoAndRecord(recorder);
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
        if (DataContext is SourceOperatorViewModel viewModel)
        {
            if (!viewModel.IsDummy.Value)
            {
                SourceOperator operation = viewModel.Model;
                Type type = operation.GetType();
                LibraryItem? item = LibraryService.Current.FindItem(type);

                if (item != null)
                {
                    headerText.Text = item.DisplayName;
                }

                if (panel.Children.Count == 2)
                {
                    panel.Children.RemoveAt(1);
                }
            }
            else
            {
                headerText.Text = Strings.Unknown;

                if (panel.Children.Count == 1)
                {
                    panel.Children.Add(new UnknownObjectView());
                }
            }
        }
    }

    private sealed class _DragBehavior : GenericDragBehavior
    {
        protected override void OnMoveDraggedItem(ItemsControl? itemsControl, int oldIndex, int newIndex)
        {
            if (itemsControl?.DataContext is SourceOperatorsTabViewModel
                {
                    Element.Value:
                    {
                        Operation.Children: { } list
                    } element
                } viewModel)
            {
                CommandRecorder recorder = viewModel.GetRequiredService<CommandRecorder>();
                list.BeginRecord<SourceOperator>()
                    .Move(oldIndex, newIndex)
                    .ToCommand([element])
                    .DoAndRecord(recorder);
            }
        }
    }
}
