using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Xaml.Interactivity;

using Beutl.Controls.Behaviors;
using Beutl.ViewModels.NodeTree;

using Microsoft.Extensions.DependencyInjection;

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

    private void RenameClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NodeInputViewModel viewModel)
        {
            var flyout = new RenameFlyout()
            {
                Text = viewModel.Node.Name
            };

            flyout.Confirmed += OnNameConfirmed;

            flyout.ShowAt(this);
        }
    }

    private void OnNameConfirmed(object? sender, string? e)
    {
        if (sender is RenameFlyout flyout
            && DataContext is NodeInputViewModel viewModel)
        {
            flyout.Confirmed -= OnNameConfirmed;
            viewModel.UpdateName(e);
        }
    }

    private sealed class _DragBehavior : GenericDragBehavior
    {
        protected override void OnMoveDraggedItem(ItemsControl? itemsControl, int oldIndex, int newIndex)
        {
            if (itemsControl?.DataContext is NodeTreeInputTabViewModel { InnerViewModel.Value: { } viewModel })
            {
                CommandRecorder recorder = viewModel.GetRequiredService<CommandRecorder>();
                oldIndex = viewModel.ConvertToOriginalIndex(oldIndex);
                newIndex = viewModel.ConvertToOriginalIndex(newIndex);
                viewModel.Model.NodeTree.Nodes.BeginRecord<Beutl.NodeTree.Node>()
                    .Move(oldIndex, newIndex)
                    .ToCommand([viewModel.Model])
                    .DoAndRecord(recorder);
            }
        }
    }
}
