using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Xaml.Interactivity;

using Beutl.Controls.Behaviors;
using Beutl.ViewModels.NodeTree;

namespace Beutl.Views.NodeTree;
public partial class NodeInputView : UserControl
{
    public NodeInputView()
    {
        InitializeComponent();
        BehaviorCollection collection = Interaction.GetBehaviors(this);
        collection.Add(new _DragBehavior()
        {
            Orientation = Orientation.Vertical,
            DragControl = dragBorder
        });
    }
    public void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NodeInputViewModel viewModel2)
        {
            viewModel2.Remove();
        }
    }

    private sealed class _DragBehavior : GenericDragBehavior
    {
        protected override void OnMoveDraggedItem(ItemsControl? itemsControl, int oldIndex, int newIndex)
        {
            if (itemsControl?.DataContext is NodeTreeInputTabViewModel { InnerViewModel.Value: { } viewModel })
            {
                oldIndex = viewModel.ConvertToOriginalIndex(oldIndex);
                newIndex = viewModel.ConvertToOriginalIndex(newIndex);
                viewModel.Model.Space.Nodes.BeginRecord<Beutl.NodeTree.Node>()
                    .Move(oldIndex, newIndex)
                    .ToCommand()
                    .DoAndRecord(CommandRecorder.Default);
            }
        }
    }
}
