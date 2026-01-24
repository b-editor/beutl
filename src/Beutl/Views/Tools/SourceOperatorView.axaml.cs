using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Xaml.Interactivity;
using Beutl.Controls.Behaviors;
using Beutl.Editor;
using Beutl.Models;
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
            HistoryManager history = viewModel2.GetRequiredService<HistoryManager>();
            SourceOperator operation = viewModel2.Model;
            Element element = operation.FindRequiredHierarchicalParent<Element>();
            element.Operation.RemoveChild(operation);
            history.Commit(CommandNames.RemoveSourceOperator);
        }
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(BeutlDataFormats.SourceOperator) is { } typeName
            && TypeFormat.ToType(typeName) is { } item2
            && DataContext is SourceOperatorViewModel viewModel2)
        {
            HistoryManager history = viewModel2.GetRequiredService<HistoryManager>();
            SourceOperator operation = viewModel2.Model;
            Element element = operation.FindRequiredHierarchicalParent<Element>();
            Rect bounds = Bounds;
            Point position = e.GetPosition(this);
            double half = bounds.Height / 2;
            int index = element.Operation.Children.IndexOf(operation);

            if (half < position.Y)
            {
                element.Operation.InsertChild(index + 1, (SourceOperator)Activator.CreateInstance(item2)!);
            }
            else
            {
                element.Operation.InsertChild(index, (SourceOperator)Activator.CreateInstance(item2)!);
            }

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

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SourceOperatorViewModel viewModel)
        {
            if (!viewModel.IsDummy.Value)
            {
                SourceOperator operation = viewModel.Model;
                Type type = operation.GetType();
                headerText.Text = TypeDisplayHelpers.GetLocalizedName(type);

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
                HistoryManager history = viewModel.GetRequiredService<HistoryManager>();
                list.Move(oldIndex, newIndex);
                history.Commit(CommandNames.MoveSourceOperator);
            }
        }
    }
}
