using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Services;
using Beutl.Services.StartupTasks;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class LifetimeCancellationSourceTests
{
    [Test]
    public void Cancel_ThrowingCallback_StillAllowsDispose()
    {
        var source = new LifetimeCancellationSource();
        using var registration = source.Token.Register(static () =>
            throw new InvalidOperationException("callback failed"));

        Assert.Throws<AggregateException>(source.Cancel);

        Assert.DoesNotThrow(source.Dispose);
    }

    [Test]
    public async Task AuthenticationTask_ShutdownCancellation_IsNotReportedAsAnError()
    {
        using var httpClient = new HttpClient();
        var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        await app.DisposeAsync();

        // RestoreUserAsync links the application lifetime, so after disposal it throws
        // ObjectDisposedException; the task must treat that as a normal shutdown and
        // complete without error telemetry.
        var task = new AuthenticationTask(app);

        await task.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task CheckForUpdatesTask_ShutdownCancellation_IsNotReportedAsAnError()
    {
        using var httpClient = new HttpClient();
        var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        await app.DisposeAsync();

        // CheckForUpdatesAsync links the application lifetime, so after disposal it throws
        // ObjectDisposedException; the task must treat that as a normal shutdown and
        // complete without error telemetry or a timeout notification.
        var task = new CheckForUpdatesTask(app);

        await task.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [AvaloniaTest]
    public async Task CheckForPackageUpdatesTask_ShutdownCancellation_IsNotReportedAsAnError()
    {
        using var httpClient = new HttpClient();
        var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var editorService = new EditorService(new ExtensionProvider());
        var projectService = new ProjectService();

        // Startup must be constructed before disposal: its constructor registers all
        // startup tasks, which lazily resolve their resources through the app.
        var startup = new Startup(app, projectService, editorService);
        var packageManager = app.GetResource<PackageManager>();
        await app.DisposeAsync();

        // CheckUpdate links the application lifetime, so after disposal it throws
        // ObjectDisposedException; the task must treat that as a normal shutdown and
        // complete without error telemetry or a notification.
        var task = new CheckForPackageUpdatesTask(startup, packageManager, app);

        await task.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
