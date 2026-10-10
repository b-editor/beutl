using System.Collections.Concurrent;
using Beutl.Services;

namespace Beutl.UnitTests.Editor.VersionControl;

// NotificationService.Handler is a process-global facade, so tests that install this capture are
// [NonParallelizable] and look for their own message instead of counting notifications.
internal sealed class VersionControlNotificationCapture : INotificationServiceHandler, IDisposable
{
    private readonly ConcurrentQueue<Notification> _notifications = new();
    private readonly INotificationServiceHandler? _previousHandler;

    private VersionControlNotificationCapture()
    {
        _previousHandler = NotificationService.Handler;
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
        // The setter rejects null, so only a real prior handler is restored.
        if (_previousHandler is not null)
        {
            NotificationService.Handler = _previousHandler;
        }
    }
}
