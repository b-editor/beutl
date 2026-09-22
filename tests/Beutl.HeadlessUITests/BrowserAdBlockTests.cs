using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Platform;
using Beutl.Editor.Components.WebBrowserTab;
using Beutl.ViewModels.SettingsPages;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class BrowserAdBlockTests
{
    [TestCase("||ads.example^", "https://ads.example/banner", true)]
    [TestCase("||ads.example^", "https://ads.example.net/banner", false)]
    [TestCase("/banner.js^", "https://cdn.test/banner.js", true)]
    [TestCase("/banner.js^", "https://cdn.test/banner.js?x=1", true)]
    [TestCase("/banner.js^", "https://cdn.test/banner.json", false)]
    [TestCase("/banner^*", "https://cdn.test/banner", true)]
    [TestCase("/banner^*", "https://cdn.test/banners", false)]
    [TestCase("/ads^file", "https://cdn.test/ads/file", true)]
    [TestCase("/ads^file", "https://cdn.test/ads", false)]
    public void WebKitSeparatorExpansionPreservesUrlBoundaries(string filter, string url, bool expected)
    {
        string regex = BrowserAdBlockRules.ToRegex(filter);
        Assert.That(Regex.IsMatch(url, regex), Is.EqualTo(expected));
        Assert.That(BrowserAdBlockRules.ToWebKitPatterns(regex).Any(pattern => Regex.IsMatch(url, pattern)), Is.EqualTo(expected));
    }

    [Test]
    public void PartyConstraintsUseFetchMetadataAndDoNotGuessSharedSuffixes()
    {
        var rules = BrowserAdBlockRules.Parse("||ads.example.co.jp^$third-party");
        var request = new Uri("https://ads.example.co.jp/banner.js");
        var page = new Uri("https://www.example.co.jp/");
        Assert.That(rules.ShouldBlock(request, page, "script", isThirdParty: false), Is.False);
        Assert.That(rules.ShouldBlock(request, page, "script", isThirdParty: true), Is.True);
        Assert.That(rules.ShouldBlock(request, page, "script"), Is.False);
    }

    [TestCase("https://ads.example.com/banner.js", true)]
    [TestCase("https://cdn.ads.example.com/banner.js", true)]
    [TestCase("https://notads.example.com/banner.js", false)]
    [TestCase("https://ads.example.com.evil.test/banner.js", false)]
    [TestCase("https://safe.test/?url=https://ads.example.com/banner.js", false)]
    [TestCase("https://ads.example.com/allowed.js", false)]
    public void NetworkRulesRespectHostBoundariesAndExceptions(string url, bool blocked)
    {
        var rules = BrowserAdBlockRules.Parse("||ads.example.com^\n@@||ads.example.com/allowed.js|");
        Assert.That(rules.ShouldBlock(new Uri(url), new Uri("https://publisher.test/"), "script"), Is.EqualTo(blocked));
    }

    [Test]
    public void OptionsArePreservedAndUnknownRestrictionsAreNotBroadened()
    {
        var rules = BrowserAdBlockRules.Parse("/banner*$script,domain=publisher.test\n||assets.test^$redirect=noopjs\n@@||ads.test^$unknown-option\nexample.test##+js(remove,ad)");
        Assert.That(rules.ShouldBlock(new Uri("https://cdn.test/banner.js"), new Uri("https://publisher.test/"), "script"), Is.True);
        Assert.That(rules.ShouldBlock(new Uri("https://cdn.test/banner.png"), new Uri("https://publisher.test/"), "image"), Is.False);
        Assert.That(rules.ShouldBlock(new Uri("https://cdn.test/banner.js"), new Uri("https://other.test/"), "script"), Is.False);
        Assert.That(rules.ShouldBlock(new Uri("https://assets.test/app.js"), new Uri("https://publisher.test/"), "script"), Is.False);
        Assert.That(rules.UnsupportedCount, Is.EqualTo(3));
        using var native = JsonDocument.Parse(rules.ToWebKitJson());
        var trigger = native.RootElement[0].GetProperty("trigger");
        Assert.That(trigger.GetProperty("if-domain")[0].GetString(), Is.EqualTo("*publisher.test"));
        Assert.That(trigger.GetProperty("resource-type")[0].GetString(), Is.EqualTo("script"));
    }

    [Test]
    public void CosmeticExceptionsRemainScopedAndFilterTextIsNotExecutable()
    {
        var rules = BrowserAdBlockRules.Parse("##.advertisement\nallowed.test#@#.advertisement\n##div[data-ad=\"</script>\"]\n||ads.test^\n@@||ads.test/allowed^");
        string script = rules.GetCosmeticScript(new Uri("https://allowed.test/"), true);
        Assert.That(script, Does.Not.Contain(".advertisement"));
        Assert.That(script, Does.Not.Contain("</script>"));
        using var native = JsonDocument.Parse(rules.ToWebKitJson());
        var cosmetic = native.RootElement[0];
        Assert.That(cosmetic.GetProperty("trigger").GetProperty("unless-domain")[0].GetString(), Is.EqualTo("*allowed.test"));
        Assert.That(native.RootElement.EnumerateArray().Last().GetProperty("action").GetProperty("type").GetString(), Is.EqualTo("ignore-previous-rules"));
    }

    [Test]
    public async Task FailedUpdatePreservesLastGoodFiltersAndOfflineCache()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var handler = new ResponseHandler();
        using var client = new HttpClient(handler);
        string path = Path.Combine(root, "filters.json");
        try
        {
            var store = new BrowserAdBlockFilterStore(path, client);
            var original = await store.UpdateAsync([BrowserAdBlockFilterStore.EasyListUrl], CancellationToken.None);
            string saved = await File.ReadAllTextAsync(path);
            handler.Response = "<html>proxy error</html>";
            Assert.ThrowsAsync<InvalidDataException>(() => store.UpdateAsync([BrowserAdBlockFilterStore.EasyListUrl], CancellationToken.None));
            Assert.That(store.Current, Is.SameAs(original));
            Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo(saved));
            int requests = handler.Requests;
            var offline = new BrowserAdBlockFilterStore(path, client);
            Assert.That((await offline.GetAsync([BrowserAdBlockFilterStore.EasyListUrl])).SupportedCount, Is.EqualTo(1));
            Assert.That(handler.Requests, Is.EqualTo(requests));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestCase("http://example.test/list.txt")]
    [TestCase("https://user:password@example.test/list.txt")]
    [TestCase("file:///tmp/list.txt")]
    [TestCase("")]
    public void ListUrlsRejectInvalidSources(string url) => Assert.Throws<InvalidDataException>(() => BrowserAdBlockFilterStore.ParseUrls(url));

    [AvaloniaTest]
    public void SettingsRollbackOnStorageFailureAndOldProfilesKeepBlockingDisabled()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "profile.json");
        try
        {
            File.WriteAllText(file, "{\"Version\":1,\"SuggestionsEnabled\":true,\"RecordDownloads\":true}");
            var profile = new BrowserProfile(file, _ => throw new IOException("read-only profile"));
            using var vm = new BrowserSettingsPageViewModel(profile, () => null);
            Assert.That(profile.BlockAds, Is.False);
            vm.BlockAds.Value = true;
            Assert.That(vm.BlockAds.Value, Is.False);
            Assert.That(profile.BlockAds, Is.False);
            Assert.That(vm.Feedback.Value, Does.Contain("read-only profile"));
            Assert.That(profile.AdBlockListUrls, Is.EqualTo(new[] { BrowserAdBlockFilterStore.EasyListUrl }));
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    public async Task DisablingDuringPreparationReleasesNavigationAndDisposesLateBackend()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var client = new HttpClient(new ResponseHandler());
        var store = new BrowserAdBlockFilterStore(Path.Combine(root, "filters.json"), client);
        try
        {
            await store.UpdateAsync([BrowserAdBlockFilterStore.EasyListUrl], CancellationToken.None);
            var profile = new BrowserProfile(Path.Combine(root, "profile.json"), adBlockFilters: store);
            profile.UpdateSettings(BrowserSearchEngine.Google, true, true, true);
            var webView = new NativeWebView();
            var pending = new TaskCompletionSource<IBrowserAdBlockBackend>();
            using var session = new BrowserAdBlockSession(webView, profile, new Uri("https://publisher.test/"), _ => Assert.Fail(), (_, _) => pending.Task);
            session.Attach(new PlatformHandle((nint)1, "test"));
            var navigation = new WebViewNavigationStartingEventArgs { Request = new Uri("https://newer.test/") };
            session.OnNavigationStarted(null, navigation);
            Assert.That(navigation.Cancel, Is.True);
            profile.UpdateSettings(BrowserSearchEngine.Google, true, true, false);
            Assert.That(session.IsPreparing, Is.False);
            Assert.That(webView.Source, Is.EqualTo(navigation.Request));
            var backend = new FakeBackend();
            pending.SetResult(backend);
            Assert.That(backend.Enabled, Is.False);
            Assert.That(backend.Disposed, Is.True);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    public async Task ClosingDuringPreparationCannotEnableBackendOrNavigate()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var client = new HttpClient(new ResponseHandler());
        var store = new BrowserAdBlockFilterStore(Path.Combine(root, "filters.json"), client);
        try
        {
            await store.UpdateAsync([BrowserAdBlockFilterStore.EasyListUrl], CancellationToken.None);
            var profile = new BrowserProfile(Path.Combine(root, "profile.json"), adBlockFilters: store);
            profile.UpdateSettings(BrowserSearchEngine.Google, true, true, true);
            var webView = new NativeWebView();
            var pending = new TaskCompletionSource<IBrowserAdBlockBackend>();
            var session = new BrowserAdBlockSession(webView, profile, new Uri("https://publisher.test/"), _ => Assert.Fail(), (_, _) => pending.Task);
            session.Attach(new PlatformHandle((nint)1, "test"));
            session.Dispose();
            var backend = new FakeBackend();
            pending.SetResult(backend);
            Assert.That(backend.Disposed, Is.True);
            Assert.That(backend.Enabled, Is.False);
            Assert.That(webView.Source.Scheme, Is.EqualTo("about"));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class FakeBackend : IBrowserAdBlockBackend
    {
        internal bool Enabled, Disposed;
        public Task EnableAsync() { Enabled = true; return Task.CompletedTask; }
        public void Dispose() => Disposed = true;
    }

    private sealed class ResponseHandler : HttpMessageHandler
    {
        internal string Response = "[Adblock Plus 2.0]\n||ads.example.test^";
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Response) });
        }
    }
}
