using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Extensibility;
using Beutl.Services;
using Beutl.Services.StartupTasks;
using Beutl.Testing.Headless;
using Beutl.ViewModels.ExtensionsPages;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AutomaticExtensionUpdateTests
{
    [AvaloniaTest]
    [TestCase(ExtensionUpdateMode.WithAppUpdate, true, true)]
    [TestCase(ExtensionUpdateMode.Always, false, true)]
    [TestCase(ExtensionUpdateMode.WithAppUpdate, true, false)]
    public async Task Verified_update_is_applied_on_the_matching_launch(ExtensionUpdateMode mode, bool appUpdate, bool compatible)
    {
        using var registration = new AppRootFileBackup("installedPackages.json");
        using var queue = new AppRootFileBackup("packageChanges.json");
        var setting = GlobalConfiguration.Instance.UpdateConfig.ExtensionUpdates;
        ExtensionUpdateMode previous = setting.Value;
        var installed = new PackageIdentity($"AutoUpdate.{Guid.NewGuid():N}", NuGetVersion.Parse("1.0.0"));
        var candidate = new PackageIdentity(installed.Id, NuGetVersion.Parse("2.0.0"));
        NuGetVersion host = NuGetVersion.Parse(BeutlApplication.Version);
        var cleanup = new List<string>
        {
            Helper.PackagePathResolver.GetInstallPath(candidate),
            Path.Combine(Helper.AppRoot, "extensionUpdates", installed.Id.ToLowerInvariant())
        };
        using var store = new UpdateStore(candidate, compatible
            ? TestPackages.CreateExtensionNupkg(candidate) : TestPackages.CreateIncompatibleNupkg(candidate, cleanup));
        using var http = new HttpClient(store, disposeHandler: false);
        try
        {
            setting.Value = mode;
            await using (var app = new BeutlApiApplication(http, new ExtensionProvider(), () => new HttpClient(store, false)))
            {
                var manager = app.GetResource<PackageManager>();
                var local = TestPackages.CreateExtracted(installed, false, loadable: true, cleanup);
                var repository = app.GetResource<InstalledPackageRepository>();
                repository.UpgradePackagesAndDeferNotifications(installed)();
                if (appUpdate)
                {
                    manager.Load(local);
                    Assert.That((await manager.StageExtensionUpdatesAsync(host, false, default)).Staged, Is.Zero);
                    Assert.That((await manager.StageExtensionUpdatesAsync(host, true, default)).Staged, Is.EqualTo(1));
                }
                else
                {
                    setting.Value = ExtensionUpdateMode.NotifyOnly;
                    var startup = new Startup(app, new ProjectService(), new EditorService(app.GetResource<ExtensionProvider>()));
                    await startup.WaitAll().WaitAsync(TimeSpan.FromSeconds(20));
                    Assert.That(store.Downloads, Is.Zero);
                    setting.Value = ExtensionUpdateMode.Always;
                    await HeadlessTestHelpers.WaitUntilAsync(() => PackageInstaller.FindStagedExtensionUpdate(installed, host, true) != null);
                }
                Assert.That((await manager.StageExtensionUpdatesAsync(host, appUpdate, default)).Staged, Is.EqualTo(1));
                Assert.That(store.Downloads, Is.EqualTo(1));
                Assert.That(repository.ExistsPackage(installed), Is.True);
                Assert.That(manager.FindLoadedPackage(installed.Id).Single().Version, Is.EqualTo("1.0.0"));
            }

            // The store can be offline on the next launch.
            using var offline = new HttpClient(new StoreHandler(TestStore.CreatePackageResponse(installed.Id)));
            await using var restarted = new BeutlApiApplication(offline, new ExtensionProvider());
            var editor = new EditorService(restarted.GetResource<ExtensionProvider>());
            var projects = new ProjectService();
            var handler = new PackageOperationHandler(restarted, editor, projects);
            Task<bool> Apply(NuGetVersion target) => handler.TryApplyStagedExtensionUpdateAsync(installed, target, () => Task.CompletedTask);
            Assert.That(await Apply(NuGetVersion.Parse("9.0.0")), Is.False);
            setting.Value = ExtensionUpdateMode.NotifyOnly;
            Assert.That(await Apply(host), Is.False);
            if (!appUpdate)
            {
                setting.Value = ExtensionUpdateMode.WithAppUpdate;
                Assert.That(await Apply(host), Is.False);
            }
            setting.Value = mode;
            handler.Queue.UninstallQueue(installed);
            Assert.That(await Apply(host), Is.False);
            handler.Queue.Cancel(installed.Id);
            if (appUpdate && compatible)
            {
                var startup = new Startup(restarted, projects, editor);
                await startup.WaitAll().WaitAsync(TimeSpan.FromSeconds(20));
                Assert.That(startup.GetTask<LoadInstalledExtensionTask>().Failures, Is.Empty);
            }
            else
            {
                Assert.That(await Apply(host), Is.EqualTo(compatible));
            }
            Assert.That(handler.InstalledPackageRepository.ExistsPackage(candidate), Is.EqualTo(compatible));
            Assert.That(handler.InstalledPackageRepository.ExistsPackage(installed), Is.EqualTo(!compatible));
            if (compatible)
            {
                Assert.That(restarted.GetResource<PackageManager>().FindLoadedPackage(installed.Id).Single().Version, Is.EqualTo("2.0.0"));
                Assert.That(PackageInstaller.FindStagedExtensionUpdate(installed, host, true), Is.Null);
            }
            else
            {
                var old = PackageManager.ReadInstalledPackage(installed)!;
                var manager = restarted.GetResource<PackageManager>();
                manager.Load(old);
                Assert.That(manager.FindLoadedPackage(installed.Id).Single().Version, Is.EqualTo("1.0.0"));
            }
        }
        finally
        {
            setting.Value = previous;
            TestPackages.Delete(cleanup);
        }
    }

    [Test]
    public async Task A_download_with_the_wrong_digest_is_never_available_to_the_next_launch()
    {
        var installed = new PackageIdentity($"AutoUpdate.{Guid.NewGuid():N}", NuGetVersion.Parse("1.0.0"));
        var candidate = new PackageIdentity(installed.Id, NuGetVersion.Parse("2.0.0"));
        using var store = new UpdateStore(candidate, TestPackages.CreateExtensionNupkg(candidate), correctHash: false);
        using var http = new HttpClient(store, false);
        await using var app = new BeutlApiApplication(http, new ExtensionProvider(), () => new HttpClient(store, false));
        var installer = app.GetResource<PackageInstaller>();
        var host = NuGetVersion.Parse(BeutlApplication.Version);
        var release = TestStore.CreateRelease(app, candidate.Version.ToString(), "archive", installed.Id);
        try
        {
            Assert.ThrowsAsync<InvalidDataException>(() => installer.StageExtensionUpdateAsync(installed, release, host, true, default));
            Assert.That(PackageInstaller.FindStagedExtensionUpdate(installed, host, true), Is.Null);
        }
        finally
        {
            installer.ClearStagedExtensionUpdates(installed);
        }
    }

    private sealed class UpdateStore(PackageIdentity candidate, byte[] archive, bool correctHash = true) : HttpMessageHandler
    {
        public int Downloads { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            HttpContent content;
            if (path.EndsWith(".nupkg", StringComparison.Ordinal))
            {
                Downloads++;
                content = new ByteArrayContent(archive);
            }
            else if (path.EndsWith("/files/archive", StringComparison.Ordinal))
                content = JsonContent.Create(new
                {
                    id = "archive",
                    name = "package.nupkg",
                    contentType = "application/octet-stream",
                    downloadUrl = "https://store.test/package.nupkg",
                    size = archive.Length,
                    sha256 = correctHash ? Convert.ToHexString(SHA256.HashData(archive)) : new string('0', 64)
                });
            else if (path.EndsWith("/releases", StringComparison.Ordinal))
                content = JsonContent.Create(new[] {
                    TestStore.CreateReleaseResponse("3.0.0", fileId: "future", targetVersion: "[999.0.0,)"),
                    TestStore.CreateReleaseResponse(candidate.Version.ToString(), fileId: "archive", targetVersion: $"[{BeutlApplication.Version}]") });
            else if (path.Contains("/releases/", StringComparison.Ordinal))
                content = JsonContent.Create(TestStore.CreateReleaseResponse("1.0.0"));
            else
                content = JsonContent.Create(TestStore.CreatePackageResponse(candidate.Id));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
