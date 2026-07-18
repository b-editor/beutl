namespace Beutl.Services;

public static class NotificationService
{
    private static INotificationServiceHandler? s_handler;

    public static INotificationServiceHandler Handler
    {
        get => s_handler!;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            s_handler = value;
        }
    }

    public static void Show(Notification notification)
    {
        s_handler?.Show(notification);
    }

    public static void Show(string title, string message,
        NotificationType type = NotificationType.Information,
        TimeSpan? expiration = null,
        Action? onClose = null,
        IReadOnlyList<NotificationAction>? actions = null)
    {
        Show(new Notification(
            title, message, type,
            expiration, onClose, actions));
    }

    public static void ShowInformation(string title, string message,
        TimeSpan? expiration = null,
        Action? onClose = null,
        IReadOnlyList<NotificationAction>? actions = null)
    {
        Show(new Notification(
            title, message, NotificationType.Information,
            expiration, onClose, actions));
    }

    public static void ShowSuccess(string title, string message,
        TimeSpan? expiration = null,
        Action? onClose = null,
        IReadOnlyList<NotificationAction>? actions = null)
    {
        Show(new Notification(
            title, message, NotificationType.Success,
            expiration, onClose, actions));
    }

    public static void ShowWarning(string title, string message,
        TimeSpan? expiration = null,
        Action? onClose = null,
        IReadOnlyList<NotificationAction>? actions = null)
    {
        Show(new Notification(
            title, message, NotificationType.Warning,
            expiration, onClose, actions));
    }

    public static void ShowError(string title, string message,
        TimeSpan? expiration = null,
        Action? onClose = null,
        IReadOnlyList<NotificationAction>? actions = null)
    {
        Show(new Notification(
            title, message, NotificationType.Error,
            expiration, onClose, actions));
    }
}

public interface INotificationServiceHandler
{
    void Show(Notification notification);
}

public enum NotificationType
{
    Information = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}

public sealed record NotificationAction(
    string Text,
    Action Callback,
    bool DismissOnInvoke = true);

public record Notification(
    string Title,
    string Message,
    NotificationType Type = NotificationType.Information,
    TimeSpan? Expiration = null,
    Action? OnClose = null,
    IReadOnlyList<NotificationAction>? Actions = null);
