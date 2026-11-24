using Avalonia;
using Beutl.Services;

namespace Beutl.Controls;

public sealed class ControlNotifications
{
    public static readonly AttachedProperty<INotificationService?> NotificationsProperty =
        AvaloniaProperty.RegisterAttached<ControlNotifications, StyledElement, INotificationService?>(
            "Notifications", inherits: true);

    public static INotificationService? GetNotifications(AvaloniaObject element) => element.GetValue(NotificationsProperty);

    public static void SetNotifications(AvaloniaObject element, INotificationService? notifications) => element.SetValue(NotificationsProperty, notifications);

    public static INotificationService GetOrDefault(AvaloniaObject element) => GetNotifications(element) ?? new NotificationService();

    // The service of the editor the element's data context belongs to, else the one its window passes down.
    internal static INotificationService FromDataContextOrDefault(StyledElement element)
        => (INotificationService?)(element.DataContext as IServiceProvider)?.GetService(typeof(INotificationService))
           ?? GetOrDefault(element);
}
