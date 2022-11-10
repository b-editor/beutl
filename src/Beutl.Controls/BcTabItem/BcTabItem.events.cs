using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

using Beutl.Controls.Extensions;

namespace Beutl.Controls;

public partial class BcTabItem
{
    public static readonly RoutedEvent<RoutedEventArgs> ClosingEvent =
        RoutedEvent.Register<BcTabItem, RoutedEventArgs>(nameof(Closing), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> CloseButtonClickEvent =
        RoutedEvent.Register<BcTabItem, RoutedEventArgs>(nameof(CloseButtonClick), RoutingStrategies.Bubble);

    protected virtual void OnClosing(object sender, RoutedEventArgs e)
    {
        IsClosing = true;
    }

    public event EventHandler<RoutedEventArgs> Closing
    {
        add => AddHandler(ClosingEvent, value);
        remove => RemoveHandler(ClosingEvent, value);
    }

    public event EventHandler<RoutedEventArgs> CloseButtonClick
    {
        add => AddHandler(CloseButtonClickEvent, value);
        remove => RemoveHandler(CloseButtonClickEvent, value);
    }
}
