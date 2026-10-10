using System.Collections.Concurrent;
using System.Reflection;
using Beutl.Services;

namespace Beutl.UnitTests.Editor.VersionControl;

// NotificationService.Handler is a process-global facade, so tests that install this capture are
// [NonParallelizable] and look for their own message instead of counting notifications.
internal sealed class VersionControlNotificationCapture : INotificationServiceHandler, IDisposable
{
    // The Handler setter rejects null, so the backing field is read and restored directly. Without
    // that, a capture installed while no handler existed would keep receiving later notifications.
    private static readonly FieldInfo s_handlerField =
        typeof(NotificationService).GetField("s_handler", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(NotificationService).FullName, "s_handler");

    private readonly ConcurrentQueue<Notification> _notifications = new();
    private readonly object? _previousHandler;

    private VersionControlNotificationCapture()
    {
        _previousHandler = s_handlerField.GetValue(null);
        NotificationService.Handler = this;
    }

    internal static VersionControlNotificationCapture Install()
    {
        return new VersionControlNotificationCapture();
    }

    internal bool HasError(string title, string message)
    {
        return _notifications.Any(notification =>
            notification.Type == NotificationType.Error
            && notification.Title == title
            && notification.Message == message);
    }

    public void Show(Notification notification)
    {
        _notifications.Enqueue(notification);
    }

    public void Dispose()
    {
        s_handlerField.SetValue(null, _previousHandler);
    }
}
