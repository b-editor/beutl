using Avalonia.Interactivity;

namespace Beutl.Controls;

public partial class BcTabView
{
    public static readonly RoutedEvent<RoutedEventArgs> ClickOnAddingButtonEvent =
        RoutedEvent.Register<BcTabView, RoutedEventArgs>(nameof(ClickOnAddingButton), RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs> ClickOnAddingButton
    {
        add => AddHandler(ClickOnAddingButtonEvent, value);
        remove => RemoveHandler(ClickOnAddingButtonEvent, value);
    }
}
