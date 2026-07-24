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
    public void PromptCaptureUnloadDiagnostics_ShowsWarningOfferingCapture_WithoutCapturingYet()
    {
        var diagnostics = new StubUnloadDiagnostics();
        PackageManager manager = CreatePackageManager(diagnostics);

        manager.PromptCaptureUnloadDiagnostics(diagnostics, "MyPlugin", ["MyPlugin.dll"]);

#if DEBUG
        Beutl.Services.Notification notification = _handler.Single();
        Assert.Multiple(() =>
        {
            Assert.That(notification.Type, Is.EqualTo(NotificationType.Warning));
            Assert.That(notification.Title, Does.Contain("MyPlugin"));
            Assert.That(notification.Actions, Is.Not.Null.With.Count.EqualTo(1));
            Assert.That(notification.Actions![0].Text, Is.EqualTo("Capture dump"));
            // The snapshot must not run just from showing the prompt; it waits for the action.
            Assert.That(diagnostics.InvokeCount, Is.EqualTo(0));
        });
#else
        // PromptCaptureUnloadDiagnostics is [Conditional("DEBUG")]: in Release builds the call is stripped.
        Assert.That(_handler.Notifications, Is.Empty);
#endif
    }

    [Test]
    public void CaptureAndOpenUnloadDump_CapturesThenOpensTheWrittenDump()
    {
        const string DumpPath = "/tmp/unload-dump-MyPlugin.txt";
        var diagnostics = new StubUnloadDiagnostics { DumpPath = DumpPath };
        PackageManager manager = CreatePackageManager(diagnostics);
        string? openedPath = null;
        manager.DumpOpener = path => openedPath = path;

        manager.CaptureAndOpenUnloadDump(diagnostics, "MyPlugin", ["MyPlugin.dll"]);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.CapturedPackage, Is.EqualTo("MyPlugin"));
            Assert.That(diagnostics.CapturedAssemblies, Is.EqualTo(new[] { "MyPlugin.dll" }));
            // The action must open exactly the path the capture returned, proving the value is threaded end to end.
            Assert.That(openedPath, Is.EqualTo(DumpPath));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    public void CaptureAndOpenUnloadDump_AcknowledgesWithoutOpening_WhenNoDumpWritten(string? dumpPath)
    {
        var diagnostics = new StubUnloadDiagnostics { DumpPath = dumpPath };
        PackageManager manager = CreatePackageManager(diagnostics);
        string? openedPath = null;
        manager.DumpOpener = path => openedPath = path;

        manager.CaptureAndOpenUnloadDump(diagnostics, "MyPlugin", ["MyPlugin.dll"]);

        // A capture that yields no dump must still tell the user something happened; the click already dismissed the
        // prompt, so a silent no-op would look broken.
        Beutl.Services.Notification notification = _handler.Single();
        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.InvokeCount, Is.EqualTo(1));
            Assert.That(openedPath, Is.Null);
            Assert.That(notification.Type, Is.EqualTo(NotificationType.Information));
            Assert.That(notification.Title, Does.Contain("MyPlugin"));
        });
    }

    private static PackageManager CreatePackageManager(ILoadContextUnloadDiagnostics diagnostics)
    {
        return new PackageManager(
            new InstalledPackageRepository(),
            new ExtensionProvider(),
            new ContextCommandManager(new ContextCommandSettingsStore(), new ContextCommandHandlerRegistry()),
            apiApplication: null!,
            unloadDiagnostics: diagnostics);
    }

    private sealed class StubUnloadDiagnostics : ILoadContextUnloadDiagnostics
    {
        public string? DumpPath { get; init; }

        public int InvokeCount { get; private set; }

        public string? CapturedPackage { get; private set; }

        public IReadOnlyList<string>? CapturedAssemblies { get; private set; }

        public string? CaptureUnloadFailure(string packageName, IReadOnlyList<string> assemblySimpleNames)
        {
            InvokeCount++;
            CapturedPackage = packageName;
            CapturedAssemblies = assemblySimpleNames;
            return DumpPath;
        }
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
