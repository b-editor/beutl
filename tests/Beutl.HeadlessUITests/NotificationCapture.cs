using System.Collections.Concurrent;
using Beutl.Services;

namespace Beutl.HeadlessUITests;

// Records the notifications shown while it is installed. Dispose puts the previous handler back.
internal sealed class NotificationCapture : INotificationServiceHandler, IDisposable
{
    private readonly INotificationServiceHandler _previousHandler = NotificationService.Handler;

    public NotificationCapture()
    {
        NotificationService.Handler = this;
    }

    public ConcurrentQueue<Notification> All { get; } = new();

    public void Show(Notification notification) => All.Enqueue(notification);

    public void Dispose() => NotificationService.Handler = _previousHandler;
}
