using Beutl.Configuration;
using Beutl.Language;
using Beutl.Services;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class StartupNotificationServiceTests
{
    private INotificationServiceHandler? _previousHandler;
    private CaptureNotificationHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _previousHandler = NotificationService.Handler;
        _handler = new CaptureNotificationHandler();
        NotificationService.Handler = _handler;
    }

    [TearDown]
    public void TearDown()
    {
        if (_previousHandler != null)
        {
            NotificationService.Handler = _previousHandler;
        }
    }

    [Test]
    public void ShowTelemetryConsent_WhenUnset_ShowsPersistentNotificationAndAccepts()
    {
        var config = new TelemetryConfig();

        StartupNotificationService.ShowTelemetryConsent(config);

        Notification notification = AssertSingleNotification();
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
        var config = new TelemetryConfig();

        StartupNotificationService.ShowTelemetryConsent(config);
        AssertSingleNotification().OnClose!.Invoke();

        AssertTelemetry(config, false);
    }

    [Test]
    public void ShowTelemetryConsent_WhenAlreadyConfigured_DoesNotShowNotification()
    {
        var config = new TelemetryConfig
        {
            Beutl_Api_Client = true,
            Beutl_Application = false,
            Beutl_PackageManagement = true,
            Beutl_Logging = false
        };

        StartupNotificationService.ShowTelemetryConsent(config);

        Assert.That(_handler.Notifications, Is.Empty);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ConfirmSideloadExtensions_CompletesFromUserChoice(bool accept)
    {
        Task<bool> result = StartupNotificationService.ConfirmSideloadExtensions(["First", "Second"]);
        Notification notification = AssertSingleNotification();

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

    private Notification AssertSingleNotification()
    {
        Assert.That(_handler.Notifications, Has.Count.EqualTo(1));
        return _handler.Notifications[0];
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
}
