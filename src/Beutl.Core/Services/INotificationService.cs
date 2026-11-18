using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

public interface INotificationService
{
    void Show(Notification notification);
}

public sealed class NotificationService(INotificationServiceHandler? handler = null) : INotificationService
{
    private static ILogger? s_logger;

    private static ILogger Logger => Log.GetLoggerOnceConfigured(ref s_logger, typeof(NotificationService));

    public void Show(Notification notification) => Dispatch(notification, handler);

    internal static void Dispatch(Notification notification, INotificationServiceHandler? handler)
    {
        int failureReported = 0;

        void ReportShowFailure()
        {
            if (Interlocked.Exchange(ref failureReported, 1) != 0)
                return;

            try
            {
                notification.OnShowFailed?.Invoke();
            }
            catch (Exception e)
            {
                Logger.LogError(
                    e,
                    "Notification OnShowFailed callback failed (Type={Type}, Title={Title})",
                    notification.Type, notification.Title);
            }
        }

        if (handler is null)
        {
            ReportShowFailure();
            return;
        }

        try
        {
            handler.Show(notification with { OnShowFailed = ReportShowFailure });
        }
        catch (Exception e)
        {
            Logger.LogError(
                e,
                "Notification handler failed (Type={Type}, Title={Title})",
                notification.Type, notification.Title);
            ReportShowFailure();
        }
    }

}

public static class NotificationServiceExtensions
{
    public static void Show(this INotificationService notifications, string title, string message,
        NotificationType type = NotificationType.Information,
        TimeSpan? expiration = null,
        Action? onClose = null,
        IReadOnlyList<NotificationAction>? actions = null,
        bool isClosable = true,
        Action? onShowFailed = null)
    {
        notifications.Show(new Notification(
            title, message, type,
            expiration, onClose, actions, isClosable, onShowFailed));
    }

    public static void ShowInformation(this INotificationService notifications, string title, string message,
        TimeSpan? expiration = null,
        Action? onClose = null,
        IReadOnlyList<NotificationAction>? actions = null,
        bool isClosable = true,
        Action? onShowFailed = null)
    {
        notifications.Show(title, message, NotificationType.Information, expiration, onClose, actions, isClosable, onShowFailed);
    }

    public static void ShowSuccess(this INotificationService notifications, string title, string message,
        TimeSpan? expiration = null,
        Action? onClose = null,
        IReadOnlyList<NotificationAction>? actions = null,
        bool isClosable = true,
        Action? onShowFailed = null)
    {
        notifications.Show(title, message, NotificationType.Success, expiration, onClose, actions, isClosable, onShowFailed);
    }

    public static void ShowWarning(this INotificationService notifications, string title, string message,
        TimeSpan? expiration = null,
        Action? onClose = null,
        IReadOnlyList<NotificationAction>? actions = null,
        bool isClosable = true,
        Action? onShowFailed = null)
    {
        notifications.Show(title, message, NotificationType.Warning, expiration, onClose, actions, isClosable, onShowFailed);
    }

    public static void ShowError(this INotificationService notifications, string title, string message,
        TimeSpan? expiration = null,
        Action? onClose = null,
        IReadOnlyList<NotificationAction>? actions = null,
        bool isClosable = true,
        Action? onShowFailed = null)
    {
        notifications.Show(title, message, NotificationType.Error, expiration, onClose, actions, isClosable, onShowFailed);
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
    IReadOnlyList<NotificationAction>? Actions = null,
    bool IsClosable = true,
    Action? OnShowFailed = null)
{
    /// <summary>Dismisses the notification when its owner cancels this token.</summary>
    public CancellationToken CancellationToken { get; init; }
}
