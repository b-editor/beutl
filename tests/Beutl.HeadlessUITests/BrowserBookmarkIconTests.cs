using System.Net;
using System.Net.Http;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Beutl.Editor.Components.WebBrowserTab;
using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Editor.Components.WebBrowserTab.Views;
using Beutl.Extensibility;

using Moq;
using SkiaSharp;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class BrowserBookmarkIconTests
{
    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task BookmarkCardsShowPngAndIcoOrKeepTheMonogramForInvalidImages(bool ico, bool invalid)
    {
        byte[] bytes = invalid ? [1, 2, 3] : CreateIcon(ico);
        using var client = new HttpClient(new IconHandler(_ => Task.FromResult(Image(bytes))));
        await WithBookmarks(new BrowserFaviconLoader(client), async (window, view) =>
        {
            var icons = view.GetVisualDescendants().OfType<BrowserBookmarkIcon>().ToArray();
            Assert.That(icons, Has.Length.EqualTo(2));
            foreach (var icon in icons)
            {
                await icon.Loading;
                Dispatcher.UIThread.RunJobs();
                Assert.That(icon.Source != null, Is.EqualTo(!invalid));
                var fallback = ((Panel)icon.Parent!).Children.OfType<TextBlock>().Single();
                Assert.That(fallback.IsVisible, Is.EqualTo(invalid));
                if (!invalid) Assert.That(((Bitmap)icon.Source!).PixelSize.Width, Is.EqualTo(48));
            }
            view.Dispose();
            Dispatcher.UIThread.RunJobs();
            Assert.That(icons.All(icon => icon.Source == null), Is.True);
        });
    }

    [AvaloniaTest]
    public async Task RebindingAndDetachingDoNotDisplayLateResults()
    {
        var firstResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new IconHandler(uri => uri.Host == "first.example"
            ? firstResponse.Task : Task.FromResult(Image(CreateIcon(false)))));
        var loader = new BrowserFaviconLoader(client);
        var icon = new BrowserBookmarkIcon { PageUrl = "https://first.example/", Loader = loader };
        var window = new Window { Content = icon };
        try
        {
            window.Show();
            Task firstLoad = icon.Loading;
            icon.PageUrl = "https://second.example/";
            await icon.Loading;
            var current = icon.Source;
            Assert.That(current, Is.Not.Null);
            firstResponse.SetResult(Image(CreateIcon(true)));
            await firstLoad;
            await loader.GetAsync("https://first.example/", default);
            Assert.That(icon.Source, Is.SameAs(current));
            window.Content = null;
            Assert.That(icon.Source, Is.Null);
            window.Content = icon;
            await icon.Loading;
            Assert.That(icon.Source, Is.Not.Null.And.Not.SameAs(current));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [Explicit("Fetches live website icons for manual visual verification.")]
    public async Task LiveWebsiteIconsAreDisplayed()
    {
        await WithBookmarks(BrowserFaviconLoader.Default, async (window, view) =>
        {
            var icons = view.GetVisualDescendants().OfType<BrowserBookmarkIcon>().ToArray();
            await Task.WhenAll(icons.Select(icon => icon.Loading));
            Assert.That(icons, Has.Length.EqualTo(2));
            Assert.That(icons.All(icon => icon.Source != null), Is.True);
            if (Environment.GetEnvironmentVariable("BEUTL_BROWSER_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                Dispatcher.UIThread.RunJobs();
                using var image = window.CaptureRenderedFrame();
                image?.Save(Path.Combine(directory, "live-bookmark-icons.png"), PngBitmapEncoderOptions.Default);
            }
        });
    }

    private static async Task WithBookmarks(BrowserFaviconLoader loader, Func<Window, WebBrowserTabView, Task> action)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var profile = new BrowserProfile(Path.Combine(root, "profile.json"));
        profile.AddBookmark(new Uri("https://www.google.com/"), "Google");
        profile.AddBookmark(new Uri("https://www.youtube.com/"), "YouTube");
        using var vm = new WebBrowserTabViewModel(new Mock<IEditorContext>().Object, WebBrowserTabViewModel.BlankPage, profile);
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (true, null, false), faviconLoader: loader);
        view.DataContext = vm;
        var window = new Window { Content = view, Width = 400, Height = 360, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            await action(window, view);
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }

    private static byte[] CreateIcon(bool ico)
    {
        using var bitmap = new SKBitmap(16, 16);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        if (!ico) return png.ToArray();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write(new byte[] { 16, 16, 0, 0 });
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write((int)png.Size);
        writer.Write(22);
        writer.Write(png.ToArray());
        return stream.ToArray();
    }

    private static HttpResponseMessage Image(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private sealed class IconHandler(Func<Uri, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request.RequestUri!);
    }
}
