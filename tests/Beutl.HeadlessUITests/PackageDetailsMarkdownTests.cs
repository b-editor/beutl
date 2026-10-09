using System.Net;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Services;
using Beutl.Converters;
using Beutl.Pages.ExtensionsPages.DiscoverPages;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels.ExtensionsPages.DiscoverPages;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;
using LiveMarkdown.Avalonia;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
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
    [TestCase(800, "light")]
    [TestCase(1100, "light")]
    [TestCase(800, "dark")]
    [TestCase(1100, "dark")]
    [TestCase(800, "high-contrast")]
    [TestCase(1100, "high-contrast")]
    public async Task DetailsRenderMarkdownAndFollowDescriptionAndReleaseChanges(int width, string theme)
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
            RequestedThemeVariant = theme switch
            {
                "dark" => ThemeVariant.Dark,
                "high-contrast" => FluentAvaloniaTheme.HighContrastTheme,
                _ => ThemeVariant.Light,
            },
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
                Assert.That(description.GetVisualDescendants().OfType<LiveMarkdown.Avalonia.CodeBlock>().Single().Language, Is.EqualTo("json"));
                Assert.That(description.CodeBlockColorTheme, Is.EqualTo(theme == "dark" ? ThemeName.DarkPlus : ThemeName.LightPlus));
                Assert.That(description.Bounds.Width, Is.GreaterThan(0).And.LessThan(width - 380));
                Assert.That(notes.Bounds.Width, Is.EqualTo(description.Bounds.Width));
            });

            if (theme == "high-contrast")
            {
                Assert.That(description.TryFindResource("SystemColorWindowTextColor", description.ActualThemeVariant, out object? foreground), Is.True);
                Assert.That(description.TryFindResource("SystemColorWindowColor", description.ActualThemeVariant, out object? background), Is.True);
                var inline = description.GetLogicalDescendants().OfType<LiveMarkdown.Avalonia.CodeInline>().Single();
                Assert.That(((Avalonia.Media.ISolidColorBrush)inline.Foreground!).Color, Is.EqualTo(foreground));
                Assert.That(((Avalonia.Media.ISolidColorBrush)inline.Background!).Color, Is.EqualTo(background));
                var language = description.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Name == "PART_LanguageTextBlock");
                var header = (Border)language.Parent!.Parent!;
                Assert.That(((Avalonia.Media.ISolidColorBrush)language.Foreground!).Color, Is.EqualTo(foreground));
                Assert.That(((Avalonia.Media.ISolidColorBrush)header.Background!).Color, Is.EqualTo(background));
            }

            if (Environment.GetEnvironmentVariable("BEUTL_MARKDOWN_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var image = window.CaptureRenderedFrame();
                Assert.That(image, Is.Not.Null);
                image!.Save(Path.Combine(directory, $"package-markdown-{width}-{theme}.png"), PngBitmapEncoderOptions.Default);
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

    [AvaloniaTest]
    [TestCase("http://127.0.0.1:12345/private.png")]
    [TestCase("https://tracking.example/preview.png")]
    [TestCase("file:///C:/Windows/private.png")]
    [TestCase("avares://Beutl/Assets/private.png")]
    [TestCase("data:image/png;base64,AAAA")]
    [TestCase("relative/preview.png")]
    public async Task PackageImagesKeepAltTextWithoutCreatingImageControls(string url)
    {
        string markdown = $"![Inline preview]({url})\n\n![Reference preview][preview]\n\n[preview]: {url}\n\n[![Linked preview]({url})](https://example.com/docs)\n\n![Outer ![Nested preview]({url})]({url})";
        using var handler = new StoreHandler { DescriptionText = markdown, ReleaseNotesText = markdown };
        using var http = new HttpClient(handler);
        var provider = new ExtensionProvider();
        await using var app = new BeutlApiApplication(http, provider);
        var package = await app.GetResource<DiscoverService>().GetPackage("Beutl.MarkdownSample", CancellationToken.None);
        using var viewModel = new PackageDetailsPageViewModel(package, app, new EditorService(provider), new ProjectService());
        await viewModel.IsBusy.FirstAsync(busy => !busy).ToTask().WaitAsync(TimeSpan.FromSeconds(10));
        var page = new PackageDetailsPage { DataContext = viewModel };
        var window = new Window { Content = page, Width = 1100, Height = 950 };
        try
        {
            window.Show();
            foreach (string name in new[] { "descriptionMarkdown", "releaseNotesMarkdown" })
            {
                var renderer = page.FindControl<MarkdownRenderer>(name)!;
                await WaitForUpdate(renderer, null);
                Assert.That(renderer.DocumentUpdate!.Document.Descendants<LinkInline>().Any(link => link.IsImage), Is.False);
                Assert.That(renderer.GetVisualDescendants().OfType<Image>(), Is.Empty,
                    "Image nodes must be removed before the renderer can invoke its image loader.");
                renderer.SelectAll();
                Assert.That(renderer.SelectedText, Does.Contain("Inline preview").And.Contain("Reference preview")
                    .And.Contain("Linked preview").And.Contain("Nested preview"));
                renderer.ClearSelection();
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    [TestCase("http://example.com/docs", false)]
    [TestCase("http://example.com/docs", true)]
    [TestCase("https://example.com/docs", false)]
    [TestCase("https://example.com/docs", true)]
    public async Task RenderedWebLinksRequireConfirmationBeforeLaunching(string url, bool open)
    {
        var launched = new List<string>();
        var page = new PackageDetailsPage(launched.Add);
        var renderer = page.FindControl<MarkdownRenderer>("descriptionMarkdown")!;
        renderer.UpdateProducer = (MarkdownUpdateProducer)MarkdownConverters.ToProducer.Convert(
            $"[Documentation]({url})", typeof(MarkdownUpdateProducer), null, System.Globalization.CultureInfo.InvariantCulture)!;
        var window = new Window { Content = page, Width = 900, Height = 600 };
        try
        {
            window.Show();
            await WaitForUpdate(renderer, null);
            var text = renderer.GetVisualDescendants().OfType<MarkdownTextBlock>().Single();
            Assert.That(launched, Is.Empty);
            Click(window, text, new Point(5, text.Bounds.Height / 2));
            await WaitUntil(() => window.GetVisualDescendants().OfType<FAContentDialog>().Any());
            var dialog = window.GetVisualDescendants().OfType<FAContentDialog>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(dialog.Title, Is.EqualTo(Beutl.Language.ExtensionsStrings.OpenUrl_Title));
                Assert.That(((SelectableTextBlock)dialog.Content!).Text, Does.Contain(url));
                Assert.That(launched, Is.Empty, "Clicking the link must only show a confirmation dialog.");
            });
            if (Environment.GetEnvironmentVariable("BEUTL_MARKDOWN_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var image = window.CaptureRenderedFrame();
                image!.Save(Path.Combine(directory, $"package-link-{new Uri(url).Scheme}-{(open ? "open" : "cancel")}.png"), PngBitmapEncoderOptions.Default);
            }

            var button = dialog.GetVisualDescendants().OfType<Button>().Single(control => control.Name == (open ? "PrimaryButton" : "CloseButton"));
            Click(window, button, new Point(button.Bounds.Width / 2, button.Bounds.Height / 2));
            await WaitUntil(() => !window.GetVisualDescendants().OfType<FAContentDialog>().Any());
            Assert.That(launched, Is.EqualTo(open ? new[] { url } : Array.Empty<string>()));
        }
        finally
        {
            foreach (var dialog in window.GetVisualDescendants().OfType<FAContentDialog>().ToArray())
                dialog.Hide();
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static void Click(Window window, Control control, Point point)
    {
        Point position = control.TranslatePoint(point, window)!.Value;
        window.MouseMove(position);
        window.MouseDown(position, MouseButton.Left);
        window.MouseUp(position, MouseButton.Left);
        HeadlessTestHelpers.Render();
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            HeadlessTestHelpers.Render();
            await Task.Delay(10, timeout.Token);
        }
        HeadlessTestHelpers.Render();
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

        public string ReleaseNotesText { get; set; } = ReleaseNotes;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object response = request.RequestUri!.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal)
                ? new[] { Release("2.0.0", ReleaseNotesText), Release("1.0.0", "Previous release"), Release("0.9.0", "") }
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
