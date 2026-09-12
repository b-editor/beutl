using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;

using Beutl.Editor.Components.WebBrowserTab;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class BrowserFaviconLoaderTests
{
    [Test]
    public async Task IconsAreCachedByOriginWithoutSendingBookmarkPathsOrQueries()
    {
        using var handler = new IconHandler(_ => Image([1, 2, 3]));
        using var client = new HttpClient(handler);
        var loader = new BrowserFaviconLoader(client);
        Assert.That(await loader.GetAsync("https://example.com:8443/private?token=secret", default), Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(await loader.GetAsync("https://example.com:8443/other", default), Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(handler.Requests, Is.EqualTo(new[] { new Uri("https://example.com:8443/favicon.ico") }));
    }

    [Test]
    public async Task MissingRootIconUsesDeclaredIconRelativeToRedirectedHomePage()
    {
        using var handler = new IconHandler(uri => uri.AbsolutePath switch
        {
            "/" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.com/app/"),
                Content = new StringContent("<link rel='icon' href='images/site.png'>")
            },
            "/app/images/site.png" => Image([4, 5]),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var client = new HttpClient(handler);
        Assert.That(await new BrowserFaviconLoader(client).GetAsync("https://example.com/saved", default), Is.EqualTo(new byte[] { 4, 5 }));
        Assert.That(handler.Requests.Last().AbsolutePath, Is.EqualTo("/app/images/site.png"));
    }

    [Test]
    public async Task CancelingOneWaiterDoesNotCancelTheSharedDownload()
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new IconHandler(_ => response.Task);
        using var client = new HttpClient(handler);
        var loader = new BrowserFaviconLoader(client);
        using var cancellation = new CancellationTokenSource();
        Task<byte[]?> first = loader.GetAsync("https://example.com/one", cancellation.Token);
        Task<byte[]?> second = loader.GetAsync("https://example.com/two", default);
        cancellation.Cancel();
        Assert.ThrowsAsync<TaskCanceledException>(async () => await first);
        response.SetResult(Image([1, 2]));
        Assert.That(await second, Is.EqualTo(new byte[] { 1, 2 }));
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
    }

    [TestCase("file:///tmp/icon.png")]
    [TestCase("https://user:secret@example.com/")]
    [TestCase("about:blank")]
    public async Task UnsupportedAddressesDoNotStartRequests(string address)
    {
        using var handler = new IconHandler(_ => Image([1]));
        using var client = new HttpClient(handler);
        Assert.That(await new BrowserFaviconLoader(client).GetAsync(address, default), Is.Null);
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task MissingIconsAreNegativelyCached()
    {
        using var handler = new IconHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        var loader = new BrowserFaviconLoader(client);
        Assert.That(await loader.GetAsync("https://example.com/", default), Is.Null);
        Assert.That(await loader.GetAsync("https://example.com/", default), Is.Null);
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OversizedImagesAreRejectedWithOrWithoutContentLength(bool streaming)
    {
        using var handler = new IconHandler(uri => uri.AbsolutePath == "/favicon.ico"
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = streaming
                    ? new StreamContent(new NonSeekableStream(new byte[BrowserFaviconLoader.MaximumBytes + 1]))
                    : new ByteArrayContent(new byte[BrowserFaviconLoader.MaximumBytes + 1])
            }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        Assert.That(await new BrowserFaviconLoader(client).GetAsync("https://example.com/", default), Is.Null);
    }

    [Test]
    public void DeclaredIconsHandleBaseUrlsEntitiesAndRelTokensButIgnoreScriptsAndUnsafeUrls()
    {
        const string html = """
            <!-- <link rel="icon" href="comment.png"> -->
            <script>const text = '<link rel="icon" href="script.png">';</script>
            <base href="/assets/">
            <LINK HREF="brand.png?x=1&amp;y=2" REL="shortcut ICON">
            <link rel='apple-touch-icon' href='//cdn.example.com/apple.png'>
            <link rel=icon href="brand.png?x=1&amp;y=2">
            <link rel=stylesheet href=style.css>
            <link rel=icon href="file:///etc/passwd">
            <link rel=icon href="https://user:secret@example.com/icon.png">
            """;
        Assert.That(BrowserFaviconLoader.FindIcons(html, new Uri("https://example.com/page")), Is.EqualTo(new[]
        {
            new Uri("https://example.com/assets/brand.png?x=1&y=2"),
            new Uri("https://cdn.example.com/apple.png")
        }));
    }

    private static HttpResponseMessage Image(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private sealed class IconHandler(Func<Uri, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public IconHandler(Func<Uri, HttpResponseMessage> respond) : this(uri => Task.FromResult(respond(uri))) { }
        internal ConcurrentQueue<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(request.RequestUri!);
            return respond(request.RequestUri!);
        }
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
