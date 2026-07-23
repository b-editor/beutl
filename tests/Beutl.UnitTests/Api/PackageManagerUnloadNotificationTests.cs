using System.Collections.Concurrent;

using Beutl.Api.Services;
using Beutl.Services;

namespace Beutl.UnitTests.Api;

// NotificationService.Handler is a process-global facade; [NonParallelizable] keeps this fixture from racing the
// other handler-swapping fixtures (SceneSettings / NotificationService).
[TestFixture]
[NonParallelizable]
public class PackageManagerUnloadNotificationTests
{
    private CaptureNotificationHandler _handler = null!;
    private INotificationServiceHandler? _previousHandler;

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
        // The setter rejects null, so only restore a real prior handler; each SetUp installs a fresh capture anyway.
        if (_previousHandler is not null)
        {
            NotificationService.Handler = _previousHandler;
        }
    }

    [Test]
    public void NotifyUnloadFailure_ShowsWarningWithOpenDumpAction_WhenDumpWritten()
    {
        const string DumpPath = "/tmp/unload-dump-MyPlugin.txt";
        PackageManager manager = CreatePackageManager();
        string? openedPath = null;
        manager.DumpOpener = path => openedPath = path;

        manager.NotifyUnloadFailure("MyPlugin", DumpPath);

#if DEBUG
        Beutl.Services.Notification notification = _handler.Single();
        Assert.Multiple(() =>
        {
            Assert.That(notification.Type, Is.EqualTo(NotificationType.Warning));
            Assert.That(notification.Title, Does.Contain("MyPlugin"));
            Assert.That(notification.Actions, Is.Not.Null.With.Count.EqualTo(1));
            Assert.That(notification.Actions![0].Text, Is.EqualTo("Open dump"));
        });

        // Invoking the action must open the exact dump path captured when the notification was built.
        notification.Actions![0].Callback();
        Assert.That(openedPath, Is.EqualTo(DumpPath));
#else
        // NotifyUnloadFailure is [Conditional("DEBUG")]: in Release builds the call is stripped, so nothing is shown.
        Assert.That(_handler.Notifications, Is.Empty);
#endif
    }

    [TestCase(null)]
    [TestCase("")]
    public void NotifyUnloadFailure_ShowsNothing_WhenNoDumpWritten(string? dumpPath)
    {
        PackageManager manager = CreatePackageManager();

        manager.NotifyUnloadFailure("MyPlugin", dumpPath);

        Assert.That(_handler.Notifications, Is.Empty);
    }

    private static PackageManager CreatePackageManager()
    {
        return new PackageManager(
            new InstalledPackageRepository(),
            new ExtensionProvider(),
            new ContextCommandManager(new ContextCommandSettingsStore(), new ContextCommandHandlerRegistry()),
            apiApplication: null!,
            unloadDiagnostics: null);
    }

    private sealed class CaptureNotificationHandler : INotificationServiceHandler
    {
        // Fully qualified: System.Reactive also defines a Notification type, so the bare name is ambiguous.
        private readonly ConcurrentQueue<Beutl.Services.Notification> _notifications = new();

        public IReadOnlyCollection<Beutl.Services.Notification> Notifications => _notifications;

        public void Show(Beutl.Services.Notification notification) => _notifications.Enqueue(notification);

        public Beutl.Services.Notification Single()
        {
            Assert.That(_notifications, Has.Count.EqualTo(1));
            return _notifications.First();
        }
    }
}
