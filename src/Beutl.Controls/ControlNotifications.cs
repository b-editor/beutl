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
}
