using System.Net;
using System.Text;
using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Services;
using Beutl.ViewModels.ExtensionsPages;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class PackageOperationLifetimeTests
{
    [Avalonia.Headless.NUnit.AvaloniaTest]
    public async Task CancellationWhilePublicationIsQueued_DoesNotPublishOrRegisterData()
    {
        await TestReset.ResetShellAsync();
        using var http = new HttpClient();
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        var operation = new PackageOperationHandler(app, new EditorService(new ExtensionProvider()), new ProjectService());
        string name = "QueuedDeployment." + Guid.NewGuid().ToString("N");
        var identity = new PackageIdentity(name, NuGetVersion.Parse("1.0.0"));
        string directory = TestPackages.CreateExtracted(identity, data: true).InstalledPath!;
        using var cancellation = new CancellationTokenSource();
        var existing = Directory.GetDirectories(Helper.AppRoot, ".data-install-*").ToHashSet();
        Task task = operation.ActivateInstalledPackageAsync(identity, cancellation.Token);
        try
        {
            Assert.That(SpinWait.SpinUntil(() => Directory.GetDirectories(Helper.AppRoot, ".data-install-*")
                .Any(path => !existing.Contains(path)), TimeSpan.FromSeconds(5)), Is.True);
            cancellation.Cancel();
            // The dispatcher is not pumped here: cancellation must complete the queued
            // operation without depending on the callback ever being executed.
            Assert.That(SpinWait.SpinUntil(() => task.IsCompleted, TimeSpan.FromSeconds(5)), Is.True);
            await Assert.CatchAsync<OperationCanceledException>(async () => await task);
            Assert.That(Directory.Exists(Path.Combine(BeutlEnvironment.GetMaterialsDirectoryPath(), name)), Is.False);
            Assert.That(app.GetResource<InstalledPackageRepository>().ExistsPackage(name, "1.0.0"), Is.False);
        }
        finally
        {
            cancellation.Cancel();
            try { await task; } catch (OperationCanceledException) { }
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public async Task ApplicationShutdown_CancelsAnInFlightPackageDownload()
    {
        using var assetHandler = new AssetHandler();
        using var http = new HttpClient(assetHandler);
        using var downloadHandler = new BlockedDownload();
        await using var app = new BeutlApiApplication(http, new ExtensionProvider(),
            () => new HttpClient(downloadHandler, disposeHandler: false));
        var operation = new PackageOperationHandler(app, new EditorService(new ExtensionProvider()), new ProjectService());
        Release release = TestStore.CreateRelease(app, "1.0.0", fileId: "archive", name: "LifetimeTest." + Guid.NewGuid().ToString("N"));
        Task install = operation.DownloadAndLoadPackage(release,
            new PackageIdentity(release.Package.Name, NuGetVersion.Parse("1.0.0")), CancellationToken.None);
        await downloadHandler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task shutdown = app.DisposeAsync().AsTask();
        try
        {
            await downloadHandler.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(install.IsCompletedSuccessfully, Is.False);
        }
        finally
        {
            downloadHandler.Abort.Cancel();
            try { await install; } catch (OperationCanceledException) { }
            await shutdown.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ImmediateInstallation_RejectsMissingHashesBeforeExtraction(bool remote)
    {
        using var assetHandler = new AssetHandler();
        using var http = new HttpClient(assetHandler);
        using var download = new MemoryDownload();
        await using var app = new BeutlApiApplication(http, new ExtensionProvider(), () => new HttpClient(download, false));
        var operation = new PackageOperationHandler(app, new EditorService(new ExtensionProvider()), new ProjectService());
        Release release = TestStore.CreateRelease(app, "1.0.0", fileId: "archive", name: "HashPolicy." + Guid.NewGuid().ToString("N"));
        var identity = new PackageIdentity(release.Package.Name, NuGetVersion.Parse("1.0.0"));
        InvalidDataException? error = await Assert.ThrowsAsync<InvalidDataException>(() => remote
            ? operation.DownloadAndLoadPackage(release, identity, CancellationToken.None)
            : operation.DownloadAndLoadPackage(identity, CancellationToken.None));
        Assert.That(error!.Message, Is.EqualTo("The package hash could not be verified."));
        Assert.That(app.GetResource<InstalledPackageRepository>().ExistsPackage(identity.Id, "1.0.0"), Is.False);
    }

    private sealed class MemoryDownload : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
    }

    private sealed class AssetHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"archive","name":"package.nupkg","contentType":"application/octet-stream","downloadUrl":"https://example.com/archive.nupkg","size":1,"sha256":null}""", Encoding.UTF8, "application/json"),
            });
    }

    private sealed class BlockedDownload : HttpMessageHandler
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly CancellationTokenSource Abort = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var registration = token.Register(() => Cancelled.TrySetResult());
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, Abort.Token);
            Entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, linked.Token);
            throw new InvalidOperationException("The download should only exit through cancellation.");
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) Abort.Dispose();
            base.Dispose(disposing);
        }
    }
}
