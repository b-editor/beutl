using System.Collections.Concurrent;
using Beutl.Services;

namespace Beutl.HeadlessUITests;

internal sealed class NotificationCapture : INotificationService, IDisposable
{
    private readonly NotificationCapture? _source;
    private readonly Action<Notification>? _onShow;
    private readonly NotificationService _service;
    private readonly bool _retain;
    private event Action<Notification>? Shown;

    public NotificationCapture(Action<Notification>? onShow = null)
        : this(onShow, retain: true)
    {
    }

    // The app-wide root forwards to the captures tests create and keeps nothing itself.
    private NotificationCapture(Action<Notification>? onShow, bool retain)
    {
        _onShow = onShow;
        _retain = retain;
        _service = new NotificationService(new CaptureHandler(Record));
    }

    public NotificationCapture(NotificationCapture source, Action<Notification>? onShow = null) : this(onShow)
    {
        _source = source;
        source.Shown += Show;
    }

    public static NotificationCapture CreateForwardingRoot() => new(null, retain: false);

    public ConcurrentQueue<Notification> All { get; } = new();

    public void Show(Notification notification) => _service.Show(notification);

    private void Record(Notification notification)
    {
        if (_retain)
            All.Enqueue(notification);
        _onShow?.Invoke(notification);
        Shown?.Invoke(notification);
    }

    public Notification Single()
    {
        Assert.That(All, Has.Count.EqualTo(1));
        return All.Single();
    }

    public void Dispose()
    {
        if (_source is not null)
            _source.Shown -= Show;
    }

    private sealed class CaptureHandler(Action<Notification> show) : INotificationServiceHandler
    {
        public void Show(Notification notification) => show(notification);
    }
}
