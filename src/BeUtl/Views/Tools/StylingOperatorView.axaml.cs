using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Xaml.Interactivity;

using BeUtl.Commands;
using BeUtl.Controls.Behaviors;
using BeUtl.ProjectSystem;
using BeUtl.Streaming;
using BeUtl.ViewModels.Tools;

namespace BeUtl.Views.Tools;

public sealed partial class StylingOperatorView : UserControl
{
    public StylingOperatorView()
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
        if (DataContext is StylingOperatorViewModel viewModel2)
        {
            StylingOperator operation = viewModel2.Model;
            Layer layer = operation.FindRequiredLogicalParent<Layer>();
            layer.RemoveChild(operation)
                .DoAndRecord(CommandRecorder.Default);
        }
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (e.Data.Get("StreamOperator") is OperatorRegistry.RegistryItem item2
            && DataContext is StylingOperatorViewModel viewModel2)
        {
            StylingOperator operation = viewModel2.Model;
            Layer layer = operation.FindRequiredLogicalParent<Layer>();
            Rect bounds = Bounds;
            Point position = e.GetPosition(this);
            double half = bounds.Height / 2;
            int index = layer.Operators.IndexOf(operation);

            if (half < position.Y)
            {
                layer.InsertChild(index + 1, (StylingOperator)Activator.CreateInstance(item2.Type)!)
                    .DoAndRecord(CommandRecorder.Default);
            }
            else
            {
                layer.InsertChild(index, (StylingOperator)Activator.CreateInstance(item2.Type)!)
                    .DoAndRecord(CommandRecorder.Default);
            }

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

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is StylingOperatorViewModel viewModel2)
        {
            StylingOperator operation = viewModel2.Model;
            Type type = operation.GetType();
            OperatorRegistry.RegistryItem? item = OperatorRegistry.FindItem(type);

            if (item != null)
            {
                headerText[!TextBlock.TextProperty] = new DynamicResourceExtension(item.DisplayName.Key);
            }
        }
    }

    private sealed class _DragBehavior : GenericDragBehavior
    {
        protected override void OnMoveDraggedItem(ItemsControl? itemsControl, int oldIndex, int newIndex)
        {
            if (itemsControl?.DataContext is StreamOperatorsTabViewModel { Layer.Value.Operators: { } list })
            {
                new MoveCommand<StreamOperator>(list, newIndex, oldIndex)
                    .DoAndRecord(CommandRecorder.Default);
            }
        }
    }
}
