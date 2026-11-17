using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Services;
using Beutl.PackageTools.UI.Models;
using Beutl.Pages;
using Beutl.Pages.ExtensionsPages;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.ExtensionsPages;
using FluentAvalonia.UI.Controls;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.HeadlessUITests;

// A notice about package updates opens the library with the update buttons already there, because
// LatestRelease is only filled by the update check.
[TestFixture, NonParallelizable]
public sealed class ExtensionsPageUpdatesTests
{
    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public async Task LibraryInstallUsesTheCompatibleVersion(bool signedIn)
    {
        await TestReset.ResetShellAsync();
        using var queueFile = new AppRootFileBackup("packageChanges.json");
        string name = $"LibraryInstall.{Guid.NewGuid():N}";
        using var store = new StoreHandler(TestStore.CreatePackageResponse(name),
            TestStore.CreateReleaseResponse("3.0.0", targetVersion: $"({BeutlApplication.Version},)"),
            TestStore.CreateReleaseResponse("2.0.0"));
        using var http = new HttpClient(store);
        await using var app = new BeutlApiApplication(http, new ExtensionProvider(TestShell.Configuration.ExtensionConfig), TestShell.Configuration.ViewConfig, TestShell.Configuration.UpdateConfig);
        if (signedIn)
            AiTestSupport.SetAuthenticatedUser(app);
        var package = await app.GetResource<DiscoverService>().GetPackage(name, CancellationToken.None);
        MainViewModel main = TestShell.MainViewModel;
        using var row = new RemoteUserPackageViewModel(package, app, main.EditorService, main.ProjectService);

        await row.Install.ExecuteAsync();

        Assert.That(app.GetResource<PackageChangesQueue>().GetInstalls(), Does.Contain(new PackageIdentity(name, NuGetVersion.Parse("2.0.0"))));
        Assert.That(store.Requests.Any(request => request.Method == HttpMethod.Post), Is.EqualTo(signedIn));
    }

    [AvaloniaTest]
    public async Task The_library_offers_the_newest_compatible_update_and_removes_it_when_the_target_changes()
    {
        await TestReset.ResetShellAsync();
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));
        string name = "Outdated." + Guid.NewGuid().ToString("N");
        var identity = new PackageIdentity(name, NuGetVersion.Parse("1.0.0"));
        var cleanup = new List<string>();
        using var registration = new AppRootFileBackup("installedPackages.json");
        using var queueFile = new AppRootFileBackup("packageChanges.json");
        TestPackages.CreateExtracted(identity, data: false, cleanup: cleanup);
        string host = BeutlApplication.Version;
        ReleaseResponse[] releases =
        [
            .. Enumerable.Range(3, 30).Reverse().Select(n => TestStore.CreateReleaseResponse($"{n}.0.0", targetVersion: $"({host},)")),
            TestStore.CreateReleaseResponse("2.0.0", targetVersion: $"[{host}]"),
            TestStore.CreateReleaseResponse("1.0.0")
        ];
        using var store = new StoreHandler(TestStore.CreatePackageResponse(name), releases);
        using var http = new HttpClient(store, disposeHandler: false);
        await using var app = new BeutlApiApplication(http, new ExtensionProvider(TestShell.Configuration.ExtensionConfig), TestShell.Configuration.ViewConfig, TestShell.Configuration.UpdateConfig);
        app.GetResource<InstalledPackageRepository>().UpgradePackagesAndDeferNotifications(identity)();
        MainViewModel main = TestShell.MainViewModel;
        using var context = new ExtensionsPageViewModel(app, main.EditorService, main.ProjectService);
        var window = new ExtensionsPage { DataContext = context, Width = 800, Height = 600 };

        try
        {
            window.OpenLibraryWithUpdates();
            window.Show();
            LibraryPageViewModel library = context.Library;
            await HeadlessTestHelpers.WaitUntilAsync(
                () => !library.IsBusy.Value
                    && library.Packages.OfType<RemoteUserPackageViewModel>().Any(row => row.IsUpdateButtonVisible.Value),
                TimeSpan.FromSeconds(20));
            HeadlessTestHelpers.Render();

            Assert.That(window.FindControl<FAFrame>("frame")!.Content, Is.InstanceOf<LibraryPage>());
            var remote = library.Packages.OfType<RemoteUserPackageViewModel>().Single(row => row.Name == name);
            var local = library.LocalPackages.Single(row => row.Name == name);
            Assert.That(remote.LatestRelease.Value?.Version.Value, Is.EqualTo("2.0.0"));
            Assert.That(local.IsUpdateButtonVisible.Value, Is.True);
            var toolsUpdate = await PackageChangeModel.TryParse(app, name, PackageChangeAction.Update, CancellationToken.None);
            Assert.That(toolsUpdate?.Version, Is.EqualTo(NuGetVersion.Parse("2.0.0")));
            HeadlessCapture.SaveIfRequested(window, nameof(ExtensionsPageUpdatesTests), "compatible-update");

            // These releases have no download, so the queue records the version the button tried to install.
            await remote.Update.ExecuteAsync();
            var queue = app.GetResource<PackageChangesQueue>();
            Assert.That(queue.GetInstalls(), Does.Contain(new PackageIdentity(name, NuGetVersion.Parse("2.0.0"))));
            queue.Cancel(name);

            releases[^2] = TestStore.CreateReleaseResponse("2.0.0", targetVersion: $"(,{host})");
            await library.CheckUpdate.ExecuteAsync();
            Assert.That(await PackageChangeModel.TryParse(app, name, PackageChangeAction.Update, CancellationToken.None), Is.Null);
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(remote.IsUpdateButtonVisible.Value, Is.False);
                Assert.That(local.IsUpdateButtonVisible.Value, Is.False);
                Assert.That(remote.IsUninstallButtonVisible.Value, Is.True);
            });
            HeadlessCapture.SaveIfRequested(window, nameof(ExtensionsPageUpdatesTests), "no-compatible-update");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
            TestPackages.Delete(cleanup);
        }
    }
}
