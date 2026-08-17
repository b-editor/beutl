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
}
