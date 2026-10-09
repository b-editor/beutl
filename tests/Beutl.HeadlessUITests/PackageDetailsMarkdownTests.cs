using System.Net;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Services;
using Beutl.Pages.ExtensionsPages.DiscoverPages;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels.ExtensionsPages.DiscoverPages;
using LiveMarkdown.Avalonia;
using TextMateSharp.Grammars;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PackageDetailsMarkdownTests
{
    private const string Description = """
        ## 拡張機能について

        **Sample Extension** adds *animated* titles and `captions` to your projects.

        - Create titles with reusable presets.
        - Customize colors, timing, and layout. Long descriptions wrap inside the card even when the extensions window is narrow.

        > Compatible with the latest version of Beutl.

        [Documentation](https://example.com/docs)

        ```json
        { "quality": "high", "enabled": true }
        ```
        """;

    private const string ReleaseNotes = """
        ### Changes

        - **Improved** title rendering.
        - Fixed the `caption` layout.

        1. Install the extension.
        2. Restart Beutl.
        """;

    [AvaloniaTest]
    [TestCase(800, false)]
    [TestCase(1100, false)]
    [TestCase(800, true)]
    [TestCase(1100, true)]
    public async Task DetailsRenderMarkdownAndFollowDescriptionAndReleaseChanges(int width, bool dark)
    {
        using var handler = new StoreHandler();
        using var http = new HttpClient(handler);
        var provider = new ExtensionProvider();
        await using var app = new BeutlApiApplication(http, provider);
        var package = await app.GetResource<DiscoverService>().GetPackage("Beutl.MarkdownSample", CancellationToken.None);
        using var viewModel = new PackageDetailsPageViewModel(package, app, new EditorService(provider), new ProjectService());
        await viewModel.IsBusy.FirstAsync(busy => !busy).ToTask().WaitAsync(TimeSpan.FromSeconds(10));
        var page = new PackageDetailsPage { DataContext = viewModel };
        var window = new Window
        {
            Content = page,
            Width = width,
            Height = 950,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
        };

        try
        {
            window.Show();
            var description = page.FindControl<MarkdownRenderer>("descriptionMarkdown")!;
            var notes = page.FindControl<MarkdownRenderer>("releaseNotesMarkdown")!;
            await WaitForUpdate(description, null);
            await WaitForUpdate(notes, null);
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(description.GetVisualDescendants().OfType<Border>().Any(control => control.Classes.Contains("Heading2Block")), Is.True);
                Assert.That(notes.GetVisualDescendants().OfType<Border>().Any(control => control.Classes.Contains("Heading3Block")), Is.True);
                Assert.That(description.GetVisualDescendants().OfType<CodeBlock>().Single().Language, Is.EqualTo("json"));
                Assert.That(description.CodeBlockColorTheme, Is.EqualTo(dark ? ThemeName.DarkPlus : ThemeName.LightPlus));
                Assert.That(description.Bounds.Width, Is.GreaterThan(0).And.LessThan(width - 380));
                Assert.That(notes.Bounds.Width, Is.EqualTo(description.Bounds.Width));
            });

            if (Environment.GetEnvironmentVariable("BEUTL_MARKDOWN_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var image = window.CaptureRenderedFrame();
                Assert.That(image, Is.Not.Null);
                image!.Save(Path.Combine(directory, $"package-markdown-{width}-{(dark ? "dark" : "light")}.png"), PngBitmapEncoderOptions.Default);
            }

            description.SelectAll();
            Assert.That(description.SelectedText, Does.Contain("Sample Extension").And.Not.Contain("**Sample Extension**"));
            description.ClearSelection();

            var previousNotes = notes.DocumentUpdate;
            viewModel.SelectedRelease.Value = viewModel.AllReleases.Single(release => release.Version.Value == "1.0.0");
            await WaitForUpdate(notes, previousNotes);
            notes.SelectAll();
            Assert.That(notes.SelectedText, Does.Contain("Previous release").And.Not.Contain("Improved"));
            notes.ClearSelection();

            previousNotes = notes.DocumentUpdate;
            viewModel.SelectedRelease.Value = viewModel.AllReleases.Single(release => release.Version.Value == "0.9.0");
            await WaitForUpdate(notes, previousNotes);
            Assert.That(notes.DocumentUpdate!.Document, Is.Empty, "A release with no notes must clear the previous version's text.");

            var previousDescription = description.DocumentUpdate;
            handler.DescriptionText = "";
            await package.RefreshAsync(CancellationToken.None);
            await WaitForUpdate(description, previousDescription);
            Assert.That(description.DocumentUpdate!.Document, Is.Empty, "Refreshing an empty description must clear the previous text.");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    [TestCase("file:///C:/Windows/example.txt")]
    [TestCase("javascript:alert(1)")]
    [TestCase("beutl://install/Beutl.Sample")]
    [TestCase("relative/page")]
    public void MarkdownLinksHandleUnsupportedUrisWithoutLaunchingThem(string url)
    {
        var page = new PackageDetailsPage();
        var renderer = page.FindControl<MarkdownRenderer>("descriptionMarkdown")!;
        var args = new LinkClickedEventArgs(MarkdownTextBlock.LinkClickEvent, renderer, new Uri(url, UriKind.RelativeOrAbsolute));
        renderer.RaiseEvent(args);
        Assert.That(args.Handled, Is.True);
    }

    private static async Task WaitForUpdate(MarkdownRenderer renderer, MarkdownDocumentUpdate? previous)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (renderer.DocumentUpdate is null || ReferenceEquals(renderer.DocumentUpdate, previous))
        {
            HeadlessTestHelpers.Settle();
            await Task.Delay(10, timeout.Token);
        }
        HeadlessTestHelpers.Render();
    }

    private sealed class StoreHandler : HttpMessageHandler
    {
        public string DescriptionText { get; set; } = Description;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object response = request.RequestUri!.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal)
                ? new[] { Release("2.0.0", ReleaseNotes), Release("1.0.0", "Previous release"), Release("0.9.0", "") }
                : new PackageResponse
                {
                    Id = "markdown-sample",
                    Name = "Beutl.MarkdownSample",
                    DisplayName = "Sample Extension",
                    Description = DescriptionText,
                    ShortDescription = "Markdown descriptions and release notes",
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

        private static ReleaseResponse Release(string version, string notes) => new()
        {
            Id = version,
            Version = version,
            Title = $"Version {version}",
            Description = notes,
            TargetVersion = null,
            FileId = null,
            FileUrl = null,
        };
    }
}
