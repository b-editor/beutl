using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;

using Beutl.Editor.Components.WebBrowserTab;
using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Editor.Components.WebBrowserTab.Views;
using Beutl.Extensibility;
using Beutl.Pages.SettingsPages;
using Beutl.ViewModels.SettingsPages;

using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class BrowserSessionViewTests
{
    [AvaloniaTest]
    public async Task DownloadUsesTheInitiatingWebViewsSessionWithoutPersistingCookies()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var profile = new BrowserProfile(Path.Combine(root, "profile.json"));
        using var vm = new WebBrowserTabViewModel(new Mock<IEditorContext>().Object, new Uri("https://media.test/page"), profile);
        var browser = new NativeWebView();
        using var view = new WebBrowserTabView(_ => browser, () => (true, null, false)) { DataContext = vm };
        using var handler = new SessionHandler();
        using var client = new HttpClient(handler);
        view.MediaDownloader = new BrowserMediaDownload(client);
        view.DownloadOptionsSelector = (_, _) => Task.FromResult<WebBrowserTabView.BrowserDownloadOptions?>(new(root, false));
        view.DownloadCookiesProvider = (source, _) =>
        {
            Assert.That(source, Is.SameAs(browser));
            return Task.FromResult<IReadOnlyList<Cookie>>(
                [new("session", "private-token", "/", "media.test") { HttpOnly = true, Secure = true },
                    new("other", "unrelated-token", "/", "other.test")]);
        };
        try
        {
            await view.DownloadMediaAsync(new Uri("https://media.test/media.mp4"), null);
            Assert.That(handler.Cookies, Is.EqualTo("session=private-token"));
            Assert.That(profile.Downloads, Has.Count.EqualTo(1));
            Assert.That(File.ReadAllText(Path.Combine(root, "profile.json")), Does.Not.Contain("private-token").And.Not.Contain("unrelated-token"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    public async Task ChangingContextWhileReadingCookiesCancelsTheDownload()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var vm = new WebBrowserTabViewModel(new Mock<IEditorContext>().Object);
        using var view = new WebBrowserTabView(_ => new NativeWebView(), () => (true, null, false)) { DataContext = vm };
        using var handler = new SessionHandler();
        using var client = new HttpClient(handler);
        view.MediaDownloader = new BrowserMediaDownload(client);
        view.DownloadOptionsSelector = (_, _) => Task.FromResult<WebBrowserTabView.BrowserDownloadOptions?>(new(root, false));
        var pending = new TaskCompletionSource<IReadOnlyList<Cookie>>();
        view.DownloadCookiesProvider = (_, token) => pending.Task.WaitAsync(token);
        Task download = view.DownloadMediaAsync(new Uri("https://media.test/media.mp4"), null);
        view.DataContext = null;
        await download.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(handler.Cookies, Is.Null);
        Assert.That(Directory.Exists(root), Is.False);
    }

    [AvaloniaTest]
    public void SettingsPageOwnsAnInvisibleProfileControlAcrossAttachAndDetach()
    {
        using var vm = new BrowserSettingsPageViewModel(new BrowserProfile(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "profile.json")), () => null);
        var page = new BrowserSettingsPage(() => true) { DataContext = vm };
        var window = new Window { Content = page };
        int registrations = 0;
        void OnChanged() => registrations++;
        BrowserWebViewRegistry.Changed += OnChanged;
        try
        {
            window.Show();
            var profileView = page.GetVisualDescendants().OfType<NativeWebView>().Single();
            Assert.That(profileView.Source, Is.EqualTo(WebBrowserTabViewModel.BlankPage));
            Assert.That(profileView.IsVisible, Is.False);
            Assert.That(registrations, Is.GreaterThan(0));
            int attached = registrations;
            window.Content = null;
            Assert.That(registrations, Is.GreaterThan(attached));
            int detached = registrations;
            window.Content = page;
            Assert.That(registrations, Is.GreaterThan(detached));
            Assert.That(page.GetVisualDescendants().OfType<NativeWebView>().Single(), Is.SameAs(profileView));
        }
        finally
        {
            BrowserWebViewRegistry.Changed -= OnChanged;
            window.Close();
        }
    }

    [AvaloniaTest]
    public void SettingsPageDoesNotCreateAProfileControlWithoutAnInstalledRuntime()
    {
        var page = new BrowserSettingsPage(() => false);
        var window = new Window { Content = page };
        try
        {
            window.Show();
            Assert.That(page.GetVisualDescendants().OfType<NativeWebView>(), Is.Empty);
        }
        finally { window.Close(); }
    }

    private sealed class SessionHandler : HttpMessageHandler
    {
        internal string? Cookies { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Cookies = request.Headers.TryGetValues("Cookie", out var values) ? string.Join("; ", values) : "";
            return Task.FromResult(new HttpResponseMessage(Cookies == "session=private-token" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized)
            {
                RequestMessage = request,
                Content = new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new MediaTypeHeaderValue("video/mp4") } }
            });
        }
    }
}
