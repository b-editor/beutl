using Avalonia.Interactivity;

namespace BEditorNext.Controls;

public partial class DraggableTabView
{
    public static readonly RoutedEvent<RoutedEventArgs> ClickOnAddingButtonEvent =
        RoutedEvent.Register<DraggableTabView, RoutedEventArgs>(nameof(ClickOnAddingButton), RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs> ClickOnAddingButton
    {
        add => AddHandler(ClickOnAddingButtonEvent, value);
        remove => RemoveHandler(ClickOnAddingButtonEvent, value);
    }
}
