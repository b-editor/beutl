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
}
