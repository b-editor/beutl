using System.Net;
using System.Text.Json;
using Beutl.Editor.Components.WebBrowserTab;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class BrowserAdBlockFilterStoreTests
{
    private const string OriginalUrl = "https://filters.example.test/original.txt";
    private const string SelectedUrl = "https://filters.example.test/selected.txt";

    [TestCase(false)]
    [TestCase(true)]
    public async Task ChangedSourcesRefreshMemoryAndDiskCaches(bool restart)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "filters.json");
        using var handler = new ResponseHandler();
        using var client = new HttpClient(handler);
        try
        {
            var store = new BrowserAdBlockFilterStore(path, client);
            await store.UpdateAsync([OriginalUrl], CancellationToken.None);
            if (restart) store = new BrowserAdBlockFilterStore(path, client);
            BrowserAdBlockRules selected = await store.GetAsync([SelectedUrl]);
            Assert.That(handler.Requests, Is.EqualTo(new[] { OriginalUrl, SelectedUrl }));
            AssertSelectedRules(selected);
            using var cache = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.That(cache.RootElement.GetProperty("Urls")[0].GetString(), Is.EqualTo(SelectedUrl));
            Assert.That(await store.GetAsync([SelectedUrl]), Is.SameAs(selected));
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SelectedSourcesAreRetriedAfterFailedUpdate(bool restart)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "filters.json");
        using var handler = new ResponseHandler();
        using var client = new HttpClient(handler);
        try
        {
            var store = new BrowserAdBlockFilterStore(path, client);
            await store.UpdateAsync([OriginalUrl], CancellationToken.None);
            string originalCache = await File.ReadAllTextAsync(path);
            handler.FailSelected = true;
            Assert.ThrowsAsync<HttpRequestException>(() => store.UpdateAsync([SelectedUrl], CancellationToken.None));
            Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo(originalCache));
            if (restart) store = new BrowserAdBlockFilterStore(path, client);
            handler.FailSelected = false;
            var rules = await store.GetAsync([SelectedUrl]);
            Assert.That(handler.Requests, Is.EqualTo(new[] { OriginalUrl, SelectedUrl, SelectedUrl }));
            AssertSelectedRules(rules);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void AssertSelectedRules(BrowserAdBlockRules rules)
    {
        var expected = BrowserAdBlockRules.Parse("[Adblock Plus 2.0]\n||selected-ad.test^");
        Assert.That(rules.SupportedCount, Is.EqualTo(1));
        Assert.That(rules.ToWebKitJson(), Is.EqualTo(expected.ToWebKitJson()));
    }

    private sealed class ResponseHandler : HttpMessageHandler
    {
        internal List<string> Requests { get; } = [];
        internal bool FailSelected;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            Requests.Add(url);
            if (FailSelected && url == SelectedUrl) throw new HttpRequestException("Subscription unavailable.");
            string domain = url == SelectedUrl ? "selected-ad.test" : "original-ad.test";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[Adblock Plus 2.0]\n||" + domain + "^")
            });
        }
    }
}
