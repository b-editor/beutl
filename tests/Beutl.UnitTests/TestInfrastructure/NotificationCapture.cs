using System.Collections.Concurrent;
using Beutl.Services;

namespace Beutl.UnitTests.TestInfrastructure;

internal sealed class NotificationCapture : INotificationService, IDisposable
{
    private readonly ConcurrentQueue<Notification> _notifications = new();

    internal IReadOnlyCollection<Notification> All => _notifications;

    internal bool HasError(string title, string message)
    {
        return _notifications.Any(notification =>
            notification.Type == NotificationType.Error
            && notification.Title == title
            && notification.Message == message);
    }

    public void Show(Notification notification) => _notifications.Enqueue(notification);

    public void Dispose()
    {
    }
}
