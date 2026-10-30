using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

using Beutl.Editor.Components.WebBrowserTab;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class BrowserDownloadReferrerTests
{
    [TestCase((int)BrowserReferrerPolicy.NoReferrer)]
    [TestCase((int)BrowserReferrerPolicy.SameOrigin)]
    public async Task DownloadReferrerPolicyAppliesToEveryRedirectWithoutDiscardingSessionCookies(int policyValue)
    {
        var policy = (BrowserReferrerPolicy)policyValue;
        using var temporary = new TemporaryDirectory();
        string directory = temporary.Path;
        using var handler = new PolicyHandler();
        using var client = new HttpClient(handler);
        var page = new Uri("https://site.example/page");
        await new BrowserMediaDownload(client).DownloadAsync(new Uri("https://site.example/start"), directory,
            null, null, default, page, [new Cookie("session", "value", "/", "site.example") { Secure = true }], policy);
        Assert.That(handler.Referrers, Is.EqualTo(new Uri?[]
            { policy == BrowserReferrerPolicy.SameOrigin ? new Uri("https://site.example/") : null, null }));
        Assert.That(handler.CookieHeaders, Is.EqualTo(new string?[] { "session=value", null }));
        var profile = new BrowserProfile(Path.Combine(directory, "profile.json"));
        profile.AddDownload(new Uri("https://site.example/start"), Path.Combine(directory, "media.mp3"), page, policy);
        var reloaded = new BrowserProfile(Path.Combine(directory, "profile.json")).Downloads.Single();
        Assert.That(reloaded.ReferrerPolicy, Is.EqualTo(policy));
        Assert.That(reloaded.Referrer, Is.EqualTo("https://site.example/"));
    }

    [Test]
    public async Task DownloadsPresentTheWebViewUserAgentOnEveryRedirect()
    {
        const string userAgent = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15";
        using var temporary = new TemporaryDirectory();
        using var handler = new PolicyHandler();
        using var client = new HttpClient(handler);
        await new BrowserMediaDownload(client).DownloadAsync(new Uri("https://site.example/start"), temporary.Path,
            null, null, default, new Uri("https://site.example/page"), userAgent: userAgent);
        Assert.That(handler.UserAgents, Is.EqualTo(new string?[] { userAgent, userAgent }));
    }

    [TestCase("https://site.example/page?secret=value#part", "https://site.example/audio.mp3", "https://site.example/")]
    [TestCase("https://site.example/page?secret=value", "https://cdn.example/audio.mp3", "https://site.example/")]
    [TestCase("https://site.example:8443/page", "https://cdn.example/audio.mp3", "https://site.example:8443/")]
    [TestCase("https://site.example/page", "http://cdn.example/audio.mp3", null)]
    [TestCase("http://site.example/page", "https://cdn.example/audio.mp3", "http://site.example/")]
    [TestCase("about:blank", "https://cdn.example/audio.mp3", null)]
    [TestCase("https://user:secret@site.example/page", "https://cdn.example/audio.mp3", null)]
    [TestCase(null, "https://cdn.example/audio.mp3", null)]
    public async Task DownloadsSendOnlyASafeInitiatingOrigin(string? source, string target, string? expected)
    {
        using var temporary = new TemporaryDirectory();
        string directory = temporary.Path;
        using var handler = new ReferrerHandler();
        using var client = new HttpClient(handler);
        await new BrowserMediaDownload(client).DownloadAsync(new Uri(target), directory, null, null, default,
            source == null ? null : new Uri(source));
        Assert.That(handler.Referrers.Single()?.AbsoluteUri, Is.EqualTo(expected));
    }

    [Test]
    public async Task SharedClientsDoNotReuseAnotherDownloadsReferrer()
    {
        using var temporary = new TemporaryDirectory();
        string directory = temporary.Path;
        using var handler = new ReferrerHandler();
        using var client = new HttpClient(handler);
        var downloader = new BrowserMediaDownload(client);
        var uri = new Uri("https://cdn.example/audio.mp3");
        await Task.WhenAll(
            downloader.DownloadAsync(uri, directory, null, null, default, new Uri("https://first.example/page")),
            downloader.DownloadAsync(uri, directory, null, null, default, new Uri("https://second.example/page")));
        await downloader.DownloadAsync(uri, directory, null, null, default);
        Assert.That(handler.Referrers.Take(2), Is.EquivalentTo(new[] { new Uri("https://first.example/"), new Uri("https://second.example/") }));
        Assert.That(handler.Referrers.Last(), Is.Null);
        Assert.That(client.DefaultRequestHeaders.Referrer, Is.Null);
    }

    private sealed class PolicyHandler : HttpMessageHandler
    {
        internal List<Uri?> Referrers { get; } = [];
        internal List<string?> CookieHeaders { get; } = [];
        internal List<string?> UserAgents { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Referrers.Add(request.Headers.Referrer);
            CookieHeaders.Add(request.Headers.TryGetValues("Cookie", out var cookies) ? string.Join("; ", cookies) : null);
            UserAgents.Add(request.Headers.TryGetValues("User-Agent", out var userAgents) ? string.Join(" ", userAgents) : null);
            if (request.RequestUri!.AbsolutePath == "/start")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found)
                { RequestMessage = request, Headers = { Location = new Uri("https://cdn.example/media.mp3") } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new MediaTypeHeaderValue("audio/mpeg") } }
            });
        }
    }

    private sealed class ReferrerHandler : HttpMessageHandler
    {
        internal ConcurrentQueue<Uri?> Referrers { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Referrers.Enqueue(request.Headers.Referrer);
            var content = new ByteArrayContent([1, 2, 3]);
            content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }
}
