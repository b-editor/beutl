using System.Collections.Concurrent;
using Beutl.Services;

namespace Beutl.HeadlessUITests;

// Records the notifications shown while it is installed. Dispose puts the previous handler back.
internal sealed class NotificationCapture : INotificationServiceHandler, IDisposable
{
    private readonly INotificationServiceHandler _previousHandler = NotificationService.Handler;
    private readonly Action<Notification>? _onShow;

    public NotificationCapture(Action<Notification>? onShow = null)
    {
        _onShow = onShow;
        NotificationService.Handler = this;
    }

    public ConcurrentQueue<Notification> All { get; } = new();

    public void Show(Notification notification)
    {
        All.Enqueue(notification);
        _onShow?.Invoke(notification);
    }

    public Notification Single()
    {
        Assert.That(All, Has.Count.EqualTo(1));
        return All.Single();
    }

    public void Dispose() => NotificationService.Handler = _previousHandler;
}
