using System.Net;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Testing.Headless;

namespace Beutl.UnitTests.Api;

[TestFixture]
[NonParallelizable]
public sealed class PackageInstallerDisposeTests
{
    [Test]
    public async Task DisposeAsync_DrainsInFlightOperations_BeforeDisposingResources()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        var handler = new BlockingHandler();
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var installer = new PackageInstaller(
            httpClient,
            ownsHttpClient: true,
            new InstalledPackageRepository(),
            app);

        var context = new PackageInstallContext(
            "Beutl.Package.DisposeTest", "1.0.0", "https://example.com/package.nupkg");
        Task download = installer.DownloadPackageFile(context, cancellationToken: CancellationToken.None);

        await handler.Entered.WaitAsync(TimeSpan.FromSeconds(10));

        Task dispose = installer.DisposeAsync().AsTask();
        await Task.Delay(500);
        Assert.That(dispose.IsCompleted, Is.False, "disposal must wait for the in-flight download");

        handler.Release();
        await dispose.WaitAsync(TimeSpan.FromSeconds(10));
        await download.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(handler.Disposed, Is.True, "the owned HttpClient must be disposed after draining");
    }

    [Test]
    public async Task DisposeAsync_DrainsCompositeInstallOperations()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var installer = new PackageInstaller(
            httpClient,
            ownsHttpClient: true,
            new InstalledPackageRepository(),
            app);

        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task operation = installer.TrackInstallOperationAsync(async () =>
        {
            operationStarted.TrySetResult();
            await releaseOperation.Task;
        });
        await operationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task dispose = installer.DisposeAsync().AsTask();
        await Task.Delay(300);
        Assert.That(dispose.IsCompleted, Is.False, "disposal must wait for the composite operation");

        releaseOperation.TrySetResult();
        await Task.WhenAll(operation, dispose).WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public bool Disposed { get; private set; }

        public void Release() => _release.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("package content")
            };
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Disposed = true;
        }
    }
}
