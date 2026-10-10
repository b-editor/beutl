using System.Reflection;
using Beutl.Services;
using Moq;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
[NonParallelizable]
public class VersionControlNotificationCaptureTests
{
    private static readonly FieldInfo s_handlerField =
        typeof(NotificationService).GetField("s_handler", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(NotificationService).FullName, "s_handler");

    [TestCase(false, TestName = "Disposed_capture_restores_a_missing_notification_handler")]
    [TestCase(true, TestName = "Disposed_capture_restores_the_previous_notification_handler")]
    public void Disposed_capture_restores_the_handler_installed_before_it(bool hadHandler)
    {
        object? original = s_handlerField.GetValue(null);
        INotificationServiceHandler? previous = hadHandler
            ? Mock.Of<INotificationServiceHandler>()
            : null;
        s_handlerField.SetValue(null, previous);
        try
        {
            VersionControlNotificationCapture capture = VersionControlNotificationCapture.Install();
            capture.Dispose();
            NotificationService.ShowError("title", "shown after the capture was disposed");

            Assert.Multiple(() =>
            {
                Assert.That(s_handlerField.GetValue(null), Is.SameAs(previous));
                Assert.That(
                    capture.HasError("title", "shown after the capture was disposed"),
                    Is.False);
            });
        }
        finally
        {
            s_handlerField.SetValue(null, original);
        }
    }
}
