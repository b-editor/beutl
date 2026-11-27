using Avalonia;
using Beutl.Services;

namespace Beutl.Controls;

public sealed partial class ControlNotifications
{
    [GeneratedAttachedProperty(Inherits = true)]
    public static partial INotificationService? GetNotifications(AvaloniaObject element);

    public static INotificationService GetOrDefault(AvaloniaObject element) => GetNotifications(element) ?? new NotificationService();

    // The service of the editor the element's data context belongs to, else the one its window passes down.
    internal static INotificationService FromDataContextOrDefault(StyledElement element)
        => (INotificationService?)(element.DataContext as IServiceProvider)?.GetService(typeof(INotificationService))
           ?? GetOrDefault(element);
}
