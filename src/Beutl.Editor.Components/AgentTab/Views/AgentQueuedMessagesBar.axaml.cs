using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Controls.Behaviors;
using Beutl.Editor.Components.AgentTab.ViewModels;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed partial class AgentQueuedMessagesBar : UserControl
{
    public AgentQueuedMessagesBar() => InitializeComponent();

    private async void OnPromote(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AgentTabViewModel viewModel && sender is Button { DataContext: AgentQueuedMessage message })
            await viewModel.PromoteQueuedMessageAsync(message);
    }

    private void OnReorderKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Up or Key.Down)
            || DataContext is not AgentTabViewModel { CanReorderQueuedMessages: true } viewModel
            || sender is not Button { DataContext: AgentQueuedMessage message })
            return;

        e.Handled = true;
        viewModel.MoveQueuedMessage(message, e.Key == Key.Up ? -1 : 1);
        Dispatcher.UIThread.Post(() =>
        {
            var handle = QueueRows.GetVisualDescendants().OfType<Button>().FirstOrDefault(button =>
                button.Name == "ReorderHandle" && ReferenceEquals(button.DataContext, message));
            handle?.Focus();
            handle?.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    private async void OnEdit(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AgentTabViewModel viewModel && sender is Button { DataContext: AgentQueuedMessage message })
        {
            await viewModel.EditQueuedMessageAsync(message);
            this.FindAncestorOfType<AgentTabView>()?.FocusComposer();
        }
    }

    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AgentTabViewModel viewModel && sender is Button { DataContext: AgentQueuedMessage message })
            viewModel.RemoveQueuedMessage(message);
    }
}

internal sealed class AgentQueuedMessageDragBehavior : GenericDragBehavior
{
    protected override void OnMoveDraggedItem(ItemsControl? itemsControl, int oldIndex, int newIndex)
    {
        if (itemsControl?.DataContext is AgentTabViewModel viewModel
            && AssociatedObject?.DataContext is AgentQueuedMessage message
            && oldIndex < itemsControl.ItemCount && ReferenceEquals(itemsControl.Items[oldIndex], message))
        {
            viewModel.MoveQueuedMessage(message, newIndex - oldIndex);
        }
    }
}
