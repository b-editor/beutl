using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Services;
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
    public async Task The_library_shows_the_update_of_an_installed_package_without_another_click()
    {
        await TestReset.ResetShellAsync();
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));
        string name = "Outdated." + Guid.NewGuid().ToString("N");
        var identity = new PackageIdentity(name, NuGetVersion.Parse("1.0.0"));
        var cleanup = new List<string>();
        using var registration = new AppRootFileBackup("installedPackages.json");
        TestPackages.CreateExtracted(identity, data: false, cleanup: cleanup);
        using var store = new StoreHandler(TestStore.CreatePackageResponse(name), TestStore.CreateReleaseResponse("2.0.0"));
        using var http = new HttpClient(store, disposeHandler: false);
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
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
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
            TestPackages.Delete(cleanup);
        }
    }
}
