using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

using Beutl.Editor.Components.WebBrowserTab;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class BrowserDownloadReferrerTests
{
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
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var handler = new ReferrerHandler();
        using var client = new HttpClient(handler);
        try
        {
            await new BrowserMediaDownload(client).DownloadAsync(new Uri(target), directory, null, null, default,
                source == null ? null : new Uri(source));
            Assert.That(handler.Referrers.Single()?.AbsoluteUri, Is.EqualTo(expected));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public async Task SharedClientsDoNotReuseAnotherDownloadsReferrer()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var handler = new ReferrerHandler();
        using var client = new HttpClient(handler);
        var downloader = new BrowserMediaDownload(client);
        var uri = new Uri("https://cdn.example/audio.mp3");
        try
        {
            await Task.WhenAll(
                downloader.DownloadAsync(uri, directory, null, null, default, new Uri("https://first.example/page")),
                downloader.DownloadAsync(uri, directory, null, null, default, new Uri("https://second.example/page")));
            await downloader.DownloadAsync(uri, directory, null, null, default);
            Assert.That(handler.Referrers.Take(2), Is.EquivalentTo(new[] { new Uri("https://first.example/"), new Uri("https://second.example/") }));
            Assert.That(handler.Referrers.Last(), Is.Null);
            Assert.That(client.DefaultRequestHeaders.Referrer, Is.Null);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    [Explicit("Downloads one public Sound Effect Lab sample for manual integration verification.")]
    public async Task SoundEffectLabDownloadUsesItsInitiatingSite()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        try
        {
            string file = await new BrowserMediaDownload(client).DownloadAsync(
                new Uri("https://soundeffect-lab.info/sound/button/mp3/decision1.mp3"), directory, null, null, default,
                new Uri("https://soundeffect-lab.info/sound/button/"));
            byte[] bytes = await File.ReadAllBytesAsync(file);
            Assert.That(Path.GetExtension(file), Is.EqualTo(".mp3"));
            Assert.That(bytes.Length, Is.GreaterThan(3));
            Assert.That(bytes.AsSpan(0, 3).SequenceEqual("ID3"u8) || (bytes[0] == 0xff && (bytes[1] & 0xe0) == 0xe0), Is.True);
            TestContext.WriteLine($"Downloaded MP3: {bytes.Length} bytes.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
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
