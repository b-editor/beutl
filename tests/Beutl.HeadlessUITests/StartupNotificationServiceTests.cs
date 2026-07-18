using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Beutl.Configuration;
using Beutl.Language;
using Beutl.Services;
using FluentAvalonia.UI.Controls;

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
        Assert.That(notification.Message, Is.EqualTo(SettingsStrings.Telemetry_Description));
        Assert.That(notification.Expiration, Is.EqualTo(Timeout.InfiniteTimeSpan));
        Assert.That(notification.IsClosable, Is.False);
        Assert.That(notification.OnClose is null, Is.True);
        Assert.That(notification.Actions, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(notification.Actions![0].Text, Is.EqualTo(Strings.ShowDetails));
            Assert.That(notification.Actions[0].DismissOnInvoke, Is.False);
            Assert.That(notification.Actions[1].Text, Is.EqualTo(Strings.Disagree));
            Assert.That(notification.Actions[1].DismissOnInvoke, Is.True);
            Assert.That(notification.Actions[2].Text, Is.EqualTo(Strings.Agree));
            Assert.That(notification.Actions[2].DismissOnInvoke, Is.True);
        });

        notification.Actions![2].Callback();

        AssertTelemetry(config, true);
    }

    [Test]
    public void ShowTelemetryConsent_WhenDisagreed_DisablesTelemetry()
    {
        using var scope = new NotificationHandlerScope();
        var config = new TelemetryConfig();

        StartupNotificationService.ShowTelemetryConsent(config);
        AssertSingleNotification(scope.Handler).Actions![1].Callback();

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
        Assert.That(notification.Actions, Has.Count.EqualTo(1));
        Assert.That(notification.Actions![0].Text, Is.EqualTo(Strings.Yes));
        Assert.That(result.IsCompleted, Is.False);

        if (accept)
        {
            notification.Actions[0].Callback();
        }
        else
        {
            notification.OnClose!.Invoke();
        }

        Assert.That(await result, Is.EqualTo(accept));
    }

    [Test]
    public void ConfirmSideloadExtensions_TruncatesLongListsAndPackageNames()
    {
        using var scope = new NotificationHandlerScope();
        string longName = new('A', StartupNotificationService.MaxSideloadPackageNameLength + 20);

        StartupNotificationService.ConfirmSideloadExtensions(
            ["First\nPackage", longName, "Third", "Fourth", "Fifth"]);
        Notification notification = AssertSingleNotification(scope.Handler);
        string[] lines = notification.Message.Split(Environment.NewLine);

        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Length.EqualTo(StartupNotificationService.MaxVisibleSideloadPackages + 1));
            Assert.That(lines[0], Is.EqualTo("First Package"));
            Assert.That(lines[1], Has.Length.EqualTo(StartupNotificationService.MaxSideloadPackageNameLength));
            Assert.That(lines[1], Does.EndWith("…"));
            Assert.That(lines[2], Is.EqualTo("Third"));
            Assert.That(lines[3], Is.EqualTo(string.Format(MessageStrings.AndMorePackages, 2)));
            Assert.That(notification.Actions, Has.Count.EqualTo(1));
            Assert.That(notification.Actions![0].Text, Is.EqualTo(Strings.Yes));
        });
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
    public void Notification_DefaultsToClosable()
    {
        var notification = new Notification("Title", "Message");

        Assert.That(notification.IsClosable, Is.True);
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

    [AvaloniaTest]
    public void BuildInfoBar_OnlyDismissesForDismissingAction()
    {
        int nonDismissingInvocations = 0;
        int dismissingInvocations = 0;
        var dismissed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notification = new Notification(
            "Title",
            "Message",
            Actions:
            [
                new("Details", () => nonDismissingInvocations++, DismissOnInvoke: false),
                new("Accept", () => dismissingInvocations++)
            ],
            IsClosable: false);
        var handler = new NotificationServiceHandler();
        InfoBar infoBar = handler.BuildInfoBar(notification, dismissed, () => { });
        var actionPanel = (WrapPanel)infoBar.ActionButton!;
        var detailsButton = (Button)actionPanel.Children[0];
        var acceptButton = (Button)actionPanel.Children[1];

        detailsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Multiple(() =>
        {
            Assert.That(nonDismissingInvocations, Is.EqualTo(1));
            Assert.That(dismissingInvocations, Is.Zero);
            Assert.That(infoBar.IsOpen, Is.True);
            Assert.That(infoBar.IsClosable, Is.False);
            Assert.That(dismissed.Task.IsCompleted, Is.False);
        });

        acceptButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Multiple(() =>
        {
            Assert.That(dismissingInvocations, Is.EqualTo(1));
            Assert.That(infoBar.IsOpen, Is.False);
            Assert.That(dismissed.Task.IsCompleted, Is.True);
        });
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
