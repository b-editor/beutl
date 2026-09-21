using System.Net;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Text.Json;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Services;
using Beutl.Pages;
using Beutl.Pages.ExtensionsPages.DiscoverPages;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.ExtensionsPages.DiscoverPages;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PackageInstallNavigationTests
{
    [AvaloniaTest]
    public async Task LinkReceivedBeforeWindowLoadsOpensTheSelectedReleaseWithoutInstalling()
    {
        await TestReset.ResetShellAsync();
        using var handler = new StoreHandler();
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

            if (Environment.GetEnvironmentVariable("BEUTL_INSTALL_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var image = window.CaptureRenderedFrame();
                image!.Save(Path.Combine(directory, "desktop-install.png"), PngBitmapEncoderOptions.Default);
            }

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
        using var handler = new StoreHandler();
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
        });
    }

    private sealed class StoreHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            object response = request.RequestUri!.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal)
                ? new[] { Release("2.0.0"), Release("1.2.3") }
                : new PackageResponse
                {
                    Id = "sample",
                    Name = "Beutl.Sample",
                    DisplayName = "Sample Extension",
                    Description = "An extension opened from the Beutl web store.",
                    ShortDescription = "Sample extension",
                    Owner = new ProfileResponse { Id = "owner", Name = "b-editor", DisplayName = "b-editor", Bio = null, IconId = null, IconUrl = null },
                    WebSite = "",
                    Tags = [],
                    LogoId = null,
                    LogoUrl = null,
                    Screenshots = [],
                    Currency = null,
                    Price = null,
                    Paid = false,
                    Owned = true,
                };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json"),
            });
        }

        private static ReleaseResponse Release(string version) => new()
        {
            Id = version,
            Version = version,
            Title = version,
            Description = "Release notes",
            TargetVersion = null,
            FileId = null,
            FileUrl = null,
        };
    }
}
