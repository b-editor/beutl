using Beutl.Configuration;
using Beutl.Language;
using Beutl.Services;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class StartupNotificationServiceTests
{
    [Test]
    public void ShowTelemetryConsent_WhenUnset_ShowsPersistentNotificationAndAccepts()
    {
        using var scope = new NotificationHandlerScope();
        var config = new TelemetryConfig();

        StartupNotificationService.ShowTelemetryConsent(config);

        Notification notification = AssertSingleNotification(scope.Handler);
        Assert.That(notification.Title, Is.EqualTo(SettingsStrings.Telemetry));
        Assert.That(notification.Message, Is.EqualTo(
            $"{SettingsStrings.Telemetry_Description}{Environment.NewLine}{StartupNotificationService.TelemetryDetailsUrl}"));
        Assert.That(notification.Expiration, Is.EqualTo(Timeout.InfiniteTimeSpan));
        Assert.That(notification.ActionButtonText, Is.EqualTo(Strings.Agree));

        notification.OnActionButtonClick!.Invoke();

        AssertTelemetry(config, true);
    }

    [Test]
    public void ShowTelemetryConsent_WhenClosed_DisablesTelemetry()
    {
        using var scope = new NotificationHandlerScope();
        var config = new TelemetryConfig();

        StartupNotificationService.ShowTelemetryConsent(config);
        AssertSingleNotification(scope.Handler).OnClose!.Invoke();

        AssertTelemetry(config, false);
    }

    [Test]
    public void ShowTelemetryConsent_WhenAlreadyConfigured_DoesNotShowNotification()
    {
        using var scope = new NotificationHandlerScope();
        var config = new TelemetryConfig
        {
            Beutl_Api_Client = true,
            Beutl_Application = false,
            Beutl_PackageManagement = true,
            Beutl_Logging = false
        };

        StartupNotificationService.ShowTelemetryConsent(config);

        Assert.That(scope.Handler.Notifications, Is.Empty);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ConfirmSideloadExtensions_CompletesFromUserChoice(bool accept)
    {
        using var scope = new NotificationHandlerScope();
        Task<bool> result = StartupNotificationService.ConfirmSideloadExtensions(["First", "Second"]);
        Notification notification = AssertSingleNotification(scope.Handler);

        Assert.That(notification.Title, Is.EqualTo(MessageStrings.ConfirmLoadSideloadExtensions));
        Assert.That(notification.Message, Is.EqualTo($"First{Environment.NewLine}Second"));
        Assert.That(notification.Expiration, Is.EqualTo(Timeout.InfiniteTimeSpan));
        Assert.That(notification.ActionButtonText, Is.EqualTo(Strings.Yes));
        Assert.That(result.IsCompleted, Is.False);

        if (accept)
        {
            notification.OnActionButtonClick!.Invoke();
        }
        else
        {
            notification.OnClose!.Invoke();
        }

        Assert.That(await result, Is.EqualTo(accept));
    }

    [Test]
    public void CreateOnceCallback_InvokesCallbackOnlyOnce()
    {
        int count = 0;
        Action callback = NotificationServiceHandler.CreateOnceCallback(() => count++);

        callback();
        callback();

        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task WaitForDismissal_CompletesPersistentDelayWhenDismissed()
    {
        var dismissed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> result = NotificationServiceHandler.WaitForDismissal(
            Timeout.InfiniteTimeSpan, dismissed.Task);

        Assert.That(result.IsCompleted, Is.False);
        dismissed.SetResult();

        Assert.That(await result, Is.True);
    }

    private static Notification AssertSingleNotification(CaptureNotificationHandler handler)
    {
        Assert.That(handler.Notifications, Has.Count.EqualTo(1));
        return handler.Notifications[0];
    }

    private static void AssertTelemetry(TelemetryConfig config, bool expected)
    {
        Assert.That(config.Beutl_Api_Client, Is.EqualTo(expected));
        Assert.That(config.Beutl_Application, Is.EqualTo(expected));
        Assert.That(config.Beutl_PackageManagement, Is.EqualTo(expected));
        Assert.That(config.Beutl_Logging, Is.EqualTo(expected));
    }

    private sealed class CaptureNotificationHandler : INotificationServiceHandler
    {
        public List<Notification> Notifications { get; } = [];

        public void Show(Notification notification) => Notifications.Add(notification);
    }

    private sealed class NotificationHandlerScope : IDisposable
    {
        private readonly INotificationServiceHandler? _previousHandler;

        public NotificationHandlerScope()
        {
            _previousHandler = NotificationService.Handler;
            Handler = new CaptureNotificationHandler();
            NotificationService.Handler = Handler;
        }

        public CaptureNotificationHandler Handler { get; }

        public void Dispose()
        {
            NotificationService.Handler = _previousHandler ?? NullNotificationHandler.Instance;
        }
    }

    private sealed class NullNotificationHandler : INotificationServiceHandler
    {
        public static NullNotificationHandler Instance { get; } = new();

        public void Show(Notification notification)
        {
        }
    }
}
