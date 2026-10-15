using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Pages;
using Beutl.Pages.ExtensionsPages.DiscoverPages;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.ExtensionsPages.DiscoverPages;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PackageInstallNavigationTests
{
    [AvaloniaTest]
    public async Task LinkReceivedBeforeWindowLoadsOpensTheSelectedReleaseWithoutInstalling()
    {
        await TestReset.ResetShellAsync();
        using StoreHandler handler = CreateHandler();
        using var http = new HttpClient(handler);
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        using var context = new ExtensionsPageViewModel(app, TestShell.MainViewModel.EditorService, TestShell.MainViewModel.ProjectService);
        var package = await app.GetResource<DiscoverService>().GetPackage("Beutl.Sample", CancellationToken.None);
        var window = new ExtensionsPage { DataContext = context };
        try
        {
            window.OpenPackage(package, "1.2.3");
            window.Show();
            HeadlessTestHelpers.Render();
            var page = window.GetVisualDescendants().OfType<PackageDetailsPage>().Single();
            var viewModel = (PackageDetailsPageViewModel)page.DataContext!;
            await viewModel.IsBusy.FirstAsync(busy => !busy).ToTask().WaitAsync(TimeSpan.FromSeconds(10));
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.SelectedRelease.Value?.Version.Value, Is.EqualTo("1.2.3"));
                Assert.That(viewModel.LatestRelease.Value?.Version.Value, Is.EqualTo("2.0.0"));
                Assert.That(viewModel.IsInstallButtonVisible.Value, Is.True);
                Assert.That(handler.Requests.All(r => r.Method == HttpMethod.Get), Is.True);
                Assert.That(handler.Requests.Any(r => r.RequestUri!.AbsolutePath.Contains("download")), Is.False);
            });

            // A second click reuses the open dialog and selects that link's version.
            window.OpenPackage(package, "2.0.0");
            HeadlessTestHelpers.Render();
            page = window.GetVisualDescendants().OfType<PackageDetailsPage>().Single();
            viewModel = (PackageDetailsPageViewModel)page.DataContext!;
            await viewModel.IsBusy.FirstAsync(busy => !busy).ToTask().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(viewModel.SelectedRelease.Value?.Version.Value, Is.EqualTo("2.0.0"));
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task MissingRequestedVersionDoesNotFallBackToInstallingLatest()
    {
        await TestReset.ResetShellAsync();
        using StoreHandler handler = CreateHandler();
        using var http = new HttpClient(handler);
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        var package = await app.GetResource<DiscoverService>().GetPackage("Beutl.Sample", CancellationToken.None);
        using var viewModel = new PackageDetailsPageViewModel(package, app,
            TestShell.MainViewModel.EditorService, TestShell.MainViewModel.ProjectService, "9.0.0");
        await viewModel.IsBusy.FirstAsync(busy => !busy).ToTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.SelectedRelease.Value, Is.Null);
            Assert.That(viewModel.CanInstallOrUpdate.Value, Is.False);
            Assert.That(viewModel.IsVersionUnsupported.Value, Is.False);
            Assert.That(viewModel.IsInstallButtonVisible.Value, Is.False);
            Assert.That(viewModel.VersionSelectionError.Value, Does.Contain("9.0.0"));
        });
    }

    [AvaloniaTest]
    public async Task MissingRequestedVersionStillResolvesTheInstalledRelease()
    {
        await TestReset.ResetShellAsync();
        using StoreHandler handler = CreateHandler();
        using var http = new HttpClient(handler);
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        var package = await app.GetResource<DiscoverService>().GetPackage("Beutl.Sample", CancellationToken.None);
        var repository = app.GetResource<InstalledPackageRepository>();
        var installed = new PackageIdentity(package.Name, NuGetVersion.Parse("1.2.3"));
        repository.UpgradePackagesAndDeferNotifications(installed)();
        using var context = new ExtensionsPageViewModel(app, TestShell.MainViewModel.EditorService, TestShell.MainViewModel.ProjectService);
        var window = new ExtensionsPage { DataContext = context };
        try
        {
            window.OpenPackage(package, "9.0.0");
            window.Show();
            HeadlessTestHelpers.Render();
            var page = window.GetVisualDescendants().OfType<PackageDetailsPage>().Single();
            var viewModel = (PackageDetailsPageViewModel)page.DataContext!;
            await viewModel.IsBusy.FirstAsync(busy => !busy).ToTask().WaitAsync(TimeSpan.FromSeconds(10));
            HeadlessTestHelpers.Settle();
            Assert.That(viewModel.CurrentRelease.Value?.Version.Value, Is.EqualTo("1.2.3"));
            var message = page.FindControl<TextBlock>("versionSelectionError")!;
            Assert.That(message.IsVisible, Is.True);
            Assert.That(message.Text, Does.Contain("9.0.0"));

            viewModel.Refresh.Execute();
            await viewModel.IsBusy.FirstAsync(busy => !busy).ToTask().WaitAsync(TimeSpan.FromSeconds(10));
            HeadlessTestHelpers.Render();
            Assert.That(message.IsVisible, Is.True);
            Assert.That(viewModel.SelectedRelease.Value, Is.Null);

            viewModel.SelectedRelease.Value = viewModel.AllReleases.Single(release => release.Version.Value == "2.0.0");
            Assert.That(viewModel.IsUpdateButtonVisible.Value, Is.True);
            Assert.That(message.IsVisible, Is.False);
            viewModel.Refresh.Execute();
            await viewModel.IsBusy.FirstAsync(busy => !busy).ToTask().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(viewModel.SelectedRelease.Value?.Version.Value, Is.EqualTo("2.0.0"));
            Assert.That(viewModel.VersionSelectionError.Value, Is.Null);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
            repository.RemovePackage(installed);
        }
    }

    private static StoreHandler CreateHandler()
        => new(TestStore.CreatePackageResponse("Beutl.Sample", "An extension opened from the Beutl web store."),
            TestStore.CreateReleaseResponse("2.0.0", "Release notes"),
            TestStore.CreateReleaseResponse("1.2.3", "Release notes"));
}
