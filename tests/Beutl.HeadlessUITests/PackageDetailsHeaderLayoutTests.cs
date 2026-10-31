using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Services;
using Beutl.Pages.ExtensionsPages.DiscoverPages;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels.ExtensionsPages.DiscoverPages;

namespace Beutl.HeadlessUITests;

// The details header puts the publisher and the package kind beside the install buttons. A long publisher
// name must shorten inside its column instead of running under the buttons, down to the window's minimum width.
[TestFixture]
public class PackageDetailsHeaderLayoutTests
{
    // ExtensionsPage.MinWidth.
    private const double WindowMinWidth = 480;

    private const string LongPublisher = "AnExtremelyLongPublisherAccountNameThatWouldRunUnderTheInstallButton";

    [AvaloniaTest]
    public async Task Long_publisher_name_stays_left_of_the_install_buttons()
    {
        var owner = new ProfileResponse
        {
            Id = "owner",
            Name = LongPublisher,
            DisplayName = LongPublisher,
            Bio = null,
            IconId = null,
            IconUrl = null,
        };
        var response = new PackageResponse
        {
            Id = "package",
            Owner = owner,
            Name = "Beutl.LongPublisher",
            DisplayName = "Package",
            Description = "",
            ShortDescription = "",
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
        using var handler = new StoreHandler(response, TestStore.CreateReleaseResponse("1.0.0"));
        using var http = new HttpClient(handler);
        var provider = new ExtensionProvider();
        await using var app = new BeutlApiApplication(http, provider);
        var package = await app.GetResource<DiscoverService>().GetPackage(response.Name, CancellationToken.None);
        using var viewModel = new PackageDetailsPageViewModel(package, app, new EditorService(provider), new ProjectService());
        await viewModel.IsBusy.FirstAsync(busy => !busy).ToTask().WaitAsync(TimeSpan.FromSeconds(10));
        var page = new PackageDetailsPage { DataContext = viewModel };
        var window = new Window { Content = page, Width = WindowMinWidth, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            List<TextBlock> blocks = page.GetVisualDescendants().OfType<TextBlock>().ToList();
            TextBlock publisher = blocks.Single(block => block.Text == LongPublisher);
            TextBlock kind = blocks.Single(block => block.Text == viewModel.KindText.Value);
            Button install = page.GetVisualDescendants().OfType<Button>().Single(button => button.Command == viewModel.Install);
            Assert.That(install.IsEffectivelyVisible, Is.True, "the header's action column must show the install button");
            double buttonsLeft = install.TranslatePoint(default, page)!.Value.X;
            double buttonsRight = install.TranslatePoint(new Point(install.Bounds.Width, 0), page)!.Value.X;

            Assert.Multiple(() =>
            {
                Assert.That(
                    buttonsRight,
                    Is.LessThanOrEqualTo(page.Bounds.Width),
                    "a long publisher name must not push the install buttons out of the page");

                foreach (TextBlock block in new[] { publisher, kind })
                {
                    double right = block.TranslatePoint(new Point(block.Bounds.Width, 0), page)!.Value.X;
                    Assert.That(right, Is.LessThanOrEqualTo(buttonsLeft), $"'{block.Text}' must end before the install buttons");
                }

                Assert.That(
                    publisher.TextTrimming,
                    Is.EqualTo(TextTrimming.CharacterEllipsis),
                    "a long publisher name must ellipsize instead of clipping");
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
