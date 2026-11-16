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
                Assert.That(PackageInstaller.FindStagedExtensionUpdate(installed, host, true), Is.Null);
                Assert.That(await Apply(host), Is.False);
                Assert.That((await manager.StageExtensionUpdatesAsync(host, true, default)).Staged, Is.Zero);
                using var nextHttp = new HttpClient(store, false);
                await using var next = new BeutlApiApplication(nextHttp, new ExtensionProvider(), () => new HttpClient(store, false));
                var release = TestStore.CreateRelease(next, "2.0.0", fileId: "archive", name: installed.Id);
                var installer = next.GetResource<PackageInstaller>();
                Assert.That(await installer.StageExtensionUpdateAsync(installed, release, host, false, default), Is.False);
                Assert.That(store.Downloads, Is.EqualTo(1));
                Assert.That(await installer.StageExtensionUpdateAsync(installed, release, new NuGetVersion(99, 0, 0), true, default), Is.True);
            }
        }
        finally
        {
            setting.Value = previous;
            TestPackages.Delete(cleanup);
        }
    }

    [AvaloniaTest]
    public async Task Ready_update_keeps_its_dependencies_for_an_offline_restart()
    {
        using var config = new AppRootFileBackup("nuget.config");
        Directory.CreateDirectory(Helper.LocalSourcePath);
        File.WriteAllText(config.Path, $"<configuration><packageSources><clear /><add key=\"Local\" value=\"{Helper.LocalSourcePath}\" /></packageSources></configuration>");
        var cleanup = new List<string>();
        var dependency = TestPackages.CreateNupkg("Required", "1.0.0", cleanup);
        string dependencyFile = Helper.GetNupkgFilePath(dependency.Id, dependency.Version.ToString());
        var installed = new PackageIdentity($"WithDependency.{Guid.NewGuid():N}", NuGetVersion.Parse("1.0.0"));
        var candidate = new PackageIdentity(installed.Id, NuGetVersion.Parse("2.0.0"));
        cleanup.Add(Helper.PackagePathResolver.GetInstallPath(candidate));
        cleanup.Add(Path.Combine(Helper.AppRoot, "extensionUpdates", installed.Id.ToLowerInvariant()));
        var host = NuGetVersion.Parse(BeutlApplication.Version);
        using var store = new UpdateStore(candidate, TestPackages.CreateExtensionNupkg(candidate, dependency: dependency));
        using var http = new HttpClient(store, false);
        try
        {
            await using (var app = new BeutlApiApplication(http, new ExtensionProvider(), () => new HttpClient(store, false)))
            {
                var installer = app.GetResource<PackageInstaller>();
                var release = TestStore.CreateRelease(app, "2.0.0", "archive", installed.Id);
                byte[] bytes = File.ReadAllBytes(dependencyFile);
                File.Delete(dependencyFile);
                await Assert.ThatAsync(() => installer.StageExtensionUpdateAsync(installed, release, host, true, default), Throws.Exception);
                Assert.That(PackageInstaller.FindStagedExtensionUpdate(installed, host, true), Is.Null);
                File.WriteAllBytes(dependencyFile, bytes);
                Assert.That(await installer.StageExtensionUpdateAsync(installed, release, host, true, default), Is.True);
                Assert.That(Helper.PackagePathResolver.GetInstalledPath(dependency), Is.Null);
            }
            File.Delete(dependencyFile);
            await using var offline = new StoreClients();
            var ready = PackageInstaller.FindStagedExtensionUpdate(installed, host, true)!;
            await offline.App.GetResource<PackageInstaller>().ResolveStagedExtensionAsync(ready, default);
            Assert.That(Helper.PackagePathResolver.GetInstalledPath(dependency), Is.Not.Null);
        }
        finally
        {
            TestPackages.Delete(cleanup);
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Upgrade_retries_the_unprepared_extension_without_holding_up_projects(bool slow)
    {
        using var registration = new AppRootFileBackup("installedPackages.json");
        var setting = GlobalConfiguration.Instance.UpdateConfig.ExtensionUpdates;
        var previous = setting.Value;
        var cleanup = new List<string>();
        var host = NuGetVersion.Parse(BeutlApplication.Version);
        var installed = new PackageIdentity($"Missing.{Guid.NewGuid():N}", NuGetVersion.Parse("1.0.0"));
        var other = new PackageIdentity($"Prepared.{Guid.NewGuid():N}", installed.Version);
        var candidate = new PackageIdentity(installed.Id, NuGetVersion.Parse("2.0.0"));
        var otherCandidate = new PackageIdentity(other.Id, candidate.Version);
        var releaseDownload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var store = new UpdateStore(candidate, TestPackages.CreateExtensionNupkg(candidate))
        {
            BeforeDownload = slow ? token => releaseDownload.Task.WaitAsync(token) : null,
        };
        using var otherStore = new UpdateStore(otherCandidate, TestPackages.CreateExtensionNupkg(otherCandidate));
        using var http = new HttpClient(store, false);
        try
        {
            setting.Value = ExtensionUpdateMode.WithAppUpdate;
            await using (var before = new BeutlApiApplication(new HttpClient(otherStore, false), new ExtensionProvider(), () => new HttpClient(otherStore, false)))
            {
                foreach (var old in new[] { installed, other })
                {
                    TestPackages.CreateExtracted(old, false, true, cleanup);
                    before.GetResource<InstalledPackageRepository>().UpgradePackagesAndDeferNotifications(old)();
                    cleanup.Add(Helper.PackagePathResolver.GetInstallPath(new PackageIdentity(old.Id, candidate.Version)));
                    cleanup.Add(Path.Combine(Helper.AppRoot, "extensionUpdates", old.Id.ToLowerInvariant()));
                }
                var release = TestStore.CreateRelease(before, "2.0.0", "archive", other.Id);
                await before.GetResource<PackageInstaller>().StageExtensionUpdateAsync(other, release, host, true, default);
            }
            await using var app = new BeutlApiApplication(http, new ExtensionProvider(), () => new HttpClient(store, false));
            var startup = new Startup(app, new ProjectService(), new EditorService(app.GetResource<ExtensionProvider>()), "0.0.1");
            var manager = app.GetResource<PackageManager>();
            try
            {
                await startup.WaitLoadingExtensions().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.That(manager.FindLoadedPackage(other.Id).Single().Version, Is.EqualTo("2.0.0"));
                Assert.That(manager.FindLoadedPackage(installed.Id).Single().Version, Is.EqualTo(slow ? "1.0.0" : "2.0.0"));
            }
            finally
            {
                releaseDownload.TrySetResult();
                await startup.WaitAll().WaitAsync(TimeSpan.FromSeconds(10));
            }
            if (slow)
                Assert.That(PackageInstaller.FindStagedExtensionUpdate(installed, host, false), Is.Not.Null);
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
            await Assert.ThatAsync(() => installer.StageExtensionUpdateAsync(installed, release, host, true, default), Throws.TypeOf<InvalidDataException>());
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
        public Func<CancellationToken, Task>? BeforeDownload { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            HttpContent content;
            if (path.EndsWith(".nupkg", StringComparison.Ordinal))
            {
                Downloads++;
                if (BeforeDownload != null) await BeforeDownload(cancellationToken);
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
                content = JsonContent.Create(TestStore.CreatePackageResponse(Path.GetFileName(path)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }
}
