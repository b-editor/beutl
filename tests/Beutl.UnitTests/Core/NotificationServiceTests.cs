using Beutl.Services;

namespace Beutl.UnitTests.Core;

[TestFixture]
public sealed class NotificationServiceTests
{
    [Test]
    public void Dispatch_WithoutHandler_InvokesShowFailed()
    {
        int failed = 0;
        var notification = new Notification("Title", "Message", OnShowFailed: () => failed++);

        NotificationService.Dispatch(notification, handler: null);

        Assert.That(failed, Is.EqualTo(1));
    }

    [Test]
    public void Dispatch_WhenHandlerThrows_InvokesShowFailedWithoutThrowing()
    {
        int failed = 0;
        var notification = new Notification("Title", "Message", OnShowFailed: () => failed++);
        var handler = new DelegateNotificationHandler(_ => throw new InvalidOperationException());

        Assert.DoesNotThrow(() => NotificationService.Dispatch(notification, handler));
        Assert.That(failed, Is.EqualTo(1));
    }

    [Test]
    public void Dispatch_WhenHandlerReportsFailureAndThrows_InvokesShowFailedOnce()
    {
        int failed = 0;
        var notification = new Notification("Title", "Message", OnShowFailed: () => failed++);
        var handler = new DelegateNotificationHandler(value =>
        {
            value.OnShowFailed!.Invoke();
            value.OnShowFailed.Invoke();
            throw new InvalidOperationException();
        });

        Assert.DoesNotThrow(() => NotificationService.Dispatch(notification, handler));
        Assert.That(failed, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Dispatch_WhenShowFailedThrows_DoesNotThrow(bool handlerThrows)
    {
        var notification = new Notification(
            "Title",
            "Message",
            OnShowFailed: () => throw new InvalidOperationException());
        INotificationServiceHandler? handler = handlerThrows
            ? new DelegateNotificationHandler(_ => throw new InvalidOperationException())
            : null;

        Assert.DoesNotThrow(() => NotificationService.Dispatch(notification, handler));
    }

    [Test]
    public void Dispatch_WhenHandlerSucceeds_DoesNotInvokeShowFailed()
    {
        int failed = 0;
        var notification = new Notification("Title", "Message", OnShowFailed: () => failed++);
        var handler = new DelegateNotificationHandler(_ => { });

        NotificationService.Dispatch(notification, handler);

        Assert.That(failed, Is.Zero);
    }

    private sealed class DelegateNotificationHandler(Action<Notification> show)
        : INotificationServiceHandler
    {
        public void Show(Notification notification)
        {
            show(notification);
        }
    }
}
