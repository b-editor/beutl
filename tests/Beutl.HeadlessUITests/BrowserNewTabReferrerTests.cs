using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;

using Beutl.Editor.Components.WebBrowserTab;
using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Editor.Components.WebBrowserTab.Views;
using Beutl.Extensibility;

using Moq;

namespace Beutl.HeadlessUITests;

// A page that opens a new tab gives the tab's first request the page's origin as its referrer, as a browser does.
// Sites that refuse hotlinks answer a download page requested without one with an error.
[TestFixture]
public class BrowserNewTabReferrerTests
{
    private static readonly Uri s_page = new("https://materials.example/sounds/detail/123?from=list");
    private static readonly Uri s_origin = new("https://materials.example/");
    private static readonly Uri s_downloadPage = new("https://downloads.example/sounds/123");

    // Opens a tab for the request from a tab that shows the page, and returns the new tab.
    private static WebBrowserTabViewModel OpenFrom(BrowserProfile profile, Uri page, Uri request,
        Action<WebBrowserTabView>? beforeRequest = null, TabOpeningEditor? editor = null)
    {
        editor ??= new TabOpeningEditor();
        using var opener = new WebBrowserTabViewModel(editor.Mock.Object, page, profile);
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (true, null, false))
        { DataContext = opener };
        beforeRequest?.Invoke(view);
        var args = new WebViewNewWindowRequestedEventArgs { Request = request };
        view.OnNewWindowRequested(null, args);
        Assert.That(args.Handled, Is.True);
        return editor.Opened!;
    }

    [AvaloniaTest]
    public void TheNewTabSendsTheOpenersOriginWithItsFirstNavigationOnly()
    {
        using var temporary = new TemporaryDirectory();
        var profile = new BrowserProfile(Path.Combine(temporary.Path, "profile.json"));
        using WebBrowserTabViewModel opened = OpenFrom(profile, s_page, s_downloadPage);
        var created = new List<Uri>();
        var navigations = new List<(Uri Uri, Uri Referrer)>();
        NativeWebView? native = null;
        using var view = new WebBrowserTabView(uri =>
            {
                created.Add(uri);
                return native = new NativeWebView { Source = uri };
            }, () => (true, null, false),
            navigateWithReferrer: (_, uri, referrer) =>
            {
                navigations.Add((uri, referrer));
                return true;
            })
        { DataContext = opened };

        // A Source would let NativeWebView request the page without the referrer as soon as its adapter exists.
        Assert.That(created, Is.EqualTo(new[] { WebBrowserTabViewModel.BlankPage }));
        Assert.That(navigations, Is.Empty);
        Assert.That(opened.CurrentUri, Is.EqualTo(s_downloadPage));
        Assert.That(opened.IsLoading.Value, Is.True);

        var adapter = new WebViewAdapterEventArgs(null);
        view.OnAdapterCreated(native, adapter);
        view.OnAdapterCreated(native, adapter);
        Assert.That(navigations, Is.EqualTo(new[] { (s_downloadPage, s_origin) }));

        // NativeWebView never saw the address, so a replacement adapter would start blank.
        view.OnAdapterDestroyed(native, adapter);
        view.OnAdapterCreated(native, adapter);
        Assert.That(navigations, Has.Count.EqualTo(2));

        // Once the page commits, NativeWebView knows its address, and nothing sends the referrer again.
        view.OnNavigationCompleted(native, new WebViewNavigationCompletedEventArgs { Request = s_downloadPage, IsSuccess = true });
        view.OnAdapterDestroyed(native, adapter);
        view.OnAdapterCreated(native, adapter);
        Assert.That(navigations, Has.Count.EqualTo(2));
    }

    [AvaloniaTest]
    public void AnHttpsPageSendsNoReferrerToAnHttpTab()
    {
        using var temporary = new TemporaryDirectory();
        var profile = new BrowserProfile(Path.Combine(temporary.Path, "profile.json"));
        var request = new Uri("http://downloads.example/sounds/123");
        using WebBrowserTabViewModel opened = OpenFrom(profile, s_page, request);
        var created = new List<Uri>();
        NativeWebView? native = null;
        using var view = new WebBrowserTabView(uri =>
            {
                created.Add(uri);
                return native = new NativeWebView { Source = uri };
            }, () => (true, null, false),
            navigateWithReferrer: (_, _, _) => throw new AssertionException("An HTTP page gets no referrer from an HTTPS page."))
        { DataContext = opened };
        view.OnAdapterCreated(native, new WebViewAdapterEventArgs(null));

        Assert.That(created, Is.EqualTo(new[] { request }));
    }

    // Once the new tab's page commits, it makes its own requests, though it may still be loading. macOS reports the
    // commit natively; WebView2 reports only main-frame starts until the page has loaded.
    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public void ThePageTheNewTabCommitsOpensWindowsWithItsOwnOrigin(bool webKit)
    {
        using var temporary = new TemporaryDirectory();
        var profile = new BrowserProfile(Path.Combine(temporary.Path, "profile.json"));
        var editor = new TabOpeningEditor();
        using WebBrowserTabViewModel opened = OpenFrom(profile, s_page, s_downloadPage, editor: editor);
        NativeWebView? native = null;
        using var view = new WebBrowserTabView(uri => native = new NativeWebView { Source = uri }, () => (true, null, false),
            navigationStartedIncludesSubframes: webKit, navigateWithReferrer: (_, _, _) => true)
        { DataContext = opened };
        view.OnAdapterCreated(native, new WebViewAdapterEventArgs(null));
        view.OnNavigationStarted(native, new WebViewNavigationStartingEventArgs { Request = s_downloadPage });
        if (webKit) view.OnNativeNavigationCommitted(s_downloadPage);

        var args = new WebViewNewWindowRequestedEventArgs { Request = new Uri("https://files.example/sounds/123") };
        view.OnNewWindowRequested(native, args);

        Assert.That(args.Handled, Is.True);
        using WebBrowserTabViewModel next = editor.Opened!;
        Assert.That(next, Is.Not.SameAs(opened));
        Assert.That(next.TakeOpenerReferrer(), Is.EqualTo(new Uri("https://downloads.example/")));
    }

    // macOS reports the referrer WebKit computed for the request, which a noreferrer link leaves out.
    [AvaloniaTest]
    [TestCase(null, null)]
    [TestCase("https://materials.example/sounds/detail/123?from=list", "https://materials.example/")]
    [TestCase("https://player.example/embed/7", "https://player.example/")]
    public void WebKitsReferrerForTheRequestDecidesTheNewTabsReferrer(string? reported, string? expected)
    {
        using var temporary = new TemporaryDirectory();
        var profile = new BrowserProfile(Path.Combine(temporary.Path, "profile.json"));
        using WebBrowserTabViewModel opened = OpenFrom(profile, s_page, s_downloadPage,
            view => view.OnNativeNewWindowRequested(s_downloadPage, reported));

        Assert.That(opened.TakeOpenerReferrer(), Is.EqualTo(expected == null ? null : new Uri(expected)));
    }

    [AvaloniaTest]
    public void AReportForAnotherRequestLeavesThePagesOrigin()
    {
        using var temporary = new TemporaryDirectory();
        var profile = new BrowserProfile(Path.Combine(temporary.Path, "profile.json"));
        using WebBrowserTabViewModel opened = OpenFrom(profile, s_page, s_downloadPage,
            view => view.OnNativeNewWindowRequested(new Uri("https://downloads.example/other"), null));

        Assert.That(opened.TakeOpenerReferrer(), Is.EqualTo(s_origin));
    }

    // On Linux the downloader requests a file again when the first navigation turns into one.
    [AvaloniaTest]
    public async Task ADownloadTheFirstNavigationTurnsIntoSendsTheOpenersOrigin()
    {
        using var temporary = new TemporaryDirectory();
        var profile = new BrowserProfile(Path.Combine(temporary.Path, "profile.json"));
        using WebBrowserTabViewModel opened = OpenFrom(profile, s_page, s_downloadPage);
        var media = new Uri("https://cdn.example/files/123.mp3");
        NativeWebView? native = null;
        // WebKitGTK reports redirects through NavigationStarted, and nothing turns a media response into a download.
        using var view = new WebBrowserTabView(uri => native = new NativeWebView { Source = uri }, () => (true, null, false),
            navigationStartedIncludesSubframes: true, handlesMediaResponsesNatively: false,
            navigateWithReferrer: (_, _, _) => true)
        { DataContext = opened };
        using var handler = new ReferrerHandler();
        using var client = new HttpClient(handler);
        view.MediaDownloader = new BrowserMediaDownload(client);
        view.DownloadOptionsSelector = (_, _) => Task.FromResult<WebBrowserTabView.BrowserDownloadOptions?>(new(temporary.Path, false));
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        profile.Downloads.CollectionChanged += (_, _) => completed.TrySetResult();
        var window = new Window { Width = 320, Height = 600, Content = view };
        try
        {
            window.Show();
            view.OnAdapterCreated(native, new WebViewAdapterEventArgs(null));
            view.OnNavigationStarted(native, new WebViewNavigationStartingEventArgs { Request = s_downloadPage });
            var redirect = new WebViewNavigationStartingEventArgs { Request = media };
            view.OnNavigationStarted(native, redirect);
            Assert.That(redirect.Cancel, Is.True);
            Dispatcher.UIThread.RunJobs();
            var confirm = view.FindControl<Button>("ConfirmPageDownloadButton")!;
            Assert.That(confirm.IsVisible, Is.True);
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(handler.Referrer, Is.EqualTo(s_origin));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task AdBlockingHoldsTheFirstNavigationUntilItsRulesAreInPlace()
    {
        using var temporary = new TemporaryDirectory();
        using var filters = new HttpClient(new BrowserAdBlockTestRules.FilterResponseHandler());
        var store = new BrowserAdBlockFilterStore(Path.Combine(temporary.Path, "filters.json"), filters);
        await store.UpdateAsync([BrowserAdBlockFilterStore.EasyListUrl], CancellationToken.None);
        var profile = new BrowserProfile(Path.Combine(temporary.Path, "profile.json"), adBlockFilters: store);
        profile.UpdateSettings(BrowserSearchEngine.Google, true, true, true);
        using WebBrowserTabViewModel opened = OpenFrom(profile, s_page, s_downloadPage);
        NativeWebView? native = null;
        using var view = new WebBrowserTabView(uri => native = new NativeWebView { Source = uri }, () => (true, null, false),
            navigateWithReferrer: (_, _, _) => throw new AssertionException("The session starts the navigation after its rules."))
        { DataContext = opened };

        view.OnAdapterCreated(native, new WebViewAdapterEventArgs(null));

        Assert.That(native!.Source, Is.EqualTo(WebBrowserTabViewModel.BlankPage));
    }

    // An editor that opens every tab a browser tab asks for and keeps the latest one.
    private sealed class TabOpeningEditor
    {
        internal TabOpeningEditor()
        {
            Mock.Setup(x => x.OpenToolTab(It.IsAny<IToolContext>()))
                .Callback<IToolContext>(context => Opened = (WebBrowserTabViewModel)context)
                .Returns(true);
        }

        internal Mock<IEditorContext> Mock { get; } = new();

        internal WebBrowserTabViewModel? Opened { get; private set; }
    }

    private sealed class ReferrerHandler : HttpMessageHandler
    {
        internal Uri? Referrer { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Referrer = request.Headers.Referrer;
            var content = new ByteArrayContent([1, 2, 3]);
            content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }
}
