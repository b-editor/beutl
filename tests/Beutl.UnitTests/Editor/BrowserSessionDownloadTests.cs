using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;

using Beutl.Editor.Components.WebBrowserTab;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class BrowserSessionDownloadTests
{
    [TestCase("https://media.example.test/media/movie.mp4", "domain=shared; host=private; secure=secret")]
    [TestCase("http://media.example.test/media/movie.mp4", "domain=shared; host=private")]
    [TestCase("https://child.media.example.test/media/movie.mp4", "domain=shared")]
    [TestCase("https://media.example.test/medial/movie.mp4", "domain=shared")]
    [TestCase("https://other.test/media/movie.mp4", "")]
    public void CookieScopePreservesHostPathExpiryAndSecureRestrictions(string address, string expected)
    {
        Cookie[] cookies =
        [
            new("domain", "shared", "/", ".example.test"),
            new("host", "private", "/media", "media.example.test") { HttpOnly = true },
            new("secure", "secret", "/media", "media.example.test") { Secure = true },
            new("expired", "old", "/", "media.example.test") { Expires = DateTime.UtcNow.AddDays(-1) },
            new("path", "other", "/private", "media.example.test"),
            new("unknown", "orphan", "/")
        ];
        var container = BrowserSessionCookies.CreateContainer(cookies, new Uri("https://media.example.test/page"));
        Assert.That(Parts(container.GetCookieHeader(new Uri(address))), Is.EquivalentTo(Parts(expected)));
        Assert.That(cookies[1].Domain, Is.EqualTo("media.example.test"));
    }

    [Test]
    public async Task RedirectsReselectCookiesAndKeepResponseCookiesWithinTheDownload()
    {
        using var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/start.mp4")
            {
                var response = Redirect("/protected/next.mp4");
                response.Headers.Add("Set-Cookie", "handshake=ready; Path=/protected; Secure");
                return response;
            }
            return request.RequestUri.Host == "source.test" ? Redirect("https://cdn.other.test/final.mp4") : Media();
        });
        using var client = new HttpClient(handler);
        var downloader = new BrowserMediaDownload(client);
        string directory = NewDirectory();
        try
        {
            await downloader.DownloadAsync(new Uri("https://source.test/start.mp4"), directory, null, null, default,
                new Uri("https://source.test/page?private=value"),
                [new("session", "authenticated", "/", "source.test"), new("cdn", "scoped", "/", "cdn.other.test")]);
            Assert.That(Parts(handler.Requests[0].Cookies), Is.EquivalentTo(new[] { "session=authenticated" }));
            Assert.That(Parts(handler.Requests[1].Cookies), Is.EquivalentTo(new[] { "session=authenticated", "handshake=ready" }));
            Assert.That(handler.Requests[2].Cookies, Is.Empty);
            Assert.That(handler.Requests.Select(x => x.Referrer), Is.All.EqualTo(new Uri("https://source.test/")));

            await downloader.DownloadAsync(new Uri("https://cdn.other.test/final.mp4"), directory, null, null, default);
            Assert.That(handler.Requests[3].Cookies, Is.Empty);
            Assert.That(client.DefaultRequestHeaders.Contains("Cookie"), Is.False);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestCase("https://target.test/media.mp4", "https://page.test/private")]
    [TestCase("http://target.test/media.mp4", "https://page.test/private")]
    public async Task CrossSiteDownloadsDoNotImportTheTargetsExistingSession(string destination, string source)
    {
        using var handler = new RecordingHandler(_ => Media());
        using var client = new HttpClient(handler);
        string directory = NewDirectory();
        try
        {
            await new BrowserMediaDownload(client).DownloadAsync(new Uri(destination), directory, null, null, default,
                new Uri(source), [new("page", "private", "/", "page.test"), new("target", "unrelated", "/", "target.test")]);
            Assert.That(handler.Requests.Single().Cookies, Is.Empty);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestCase("http://source.test/final.mp4")]
    [TestCase("https://user:secret@source.test/final.mp4")]
    [TestCase("file:///tmp/movie.mp4")]
    public void UnsafeRedirectsAreRejectedBeforeSendingAnotherRequest(string destination)
    {
        using var handler = new RecordingHandler(_ => Redirect(destination));
        using var client = new HttpClient(handler);
        string directory = NewDirectory();
        try
        {
            Assert.ThrowsAsync<InvalidOperationException>(() => new BrowserMediaDownload(client).DownloadAsync(
                new Uri("https://source.test/start.mp4"), directory, null, null, default,
                cookies: [new("session", "secret", "/", "source.test")]));
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
            Assert.That(Directory.Exists(directory), Is.False);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public void RedirectLoopsAreBounded()
    {
        using var handler = new RecordingHandler(_ => Redirect("/loop.mp4"));
        using var client = new HttpClient(handler);
        Assert.ThrowsAsync<HttpRequestException>(() => new BrowserMediaDownload(client).DownloadAsync(
            new Uri("https://source.test/loop.mp4"), NewDirectory(), null, null, default));
        Assert.That(handler.Requests, Has.Count.EqualTo(11));
    }

    [Test]
    public async Task DefaultTransportAuthenticatesWithoutForwardingCookiesToAnotherHost()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var headers = new List<string>();
        string directory = NewDirectory();
        Task server = ServeAsync();
        try
        {
            await BrowserMediaDownload.Default.DownloadAsync(new Uri($"http://localhost:{port}/start.mp4"), directory,
                null, null, timeout.Token, cookies: [new("session", "signed-in", "/", "localhost")]);
            await server;
            Assert.That(headers[0], Does.Contain("session=signed-in"));
            Assert.That(headers[1], Does.Contain("session=signed-in").And.Contain("handshake=ready"));
            Assert.That(headers[2], Does.Not.Contain("Cookie:"));
            Assert.That(File.ReadAllBytes(Directory.GetFiles(directory).Single()), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }
        finally
        {
            timeout.Cancel();
            listener.Stop();
            try { await server; } catch (Exception) when (timeout.IsCancellationRequested) { }
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        async Task ServeAsync()
        {
            for (int index = 0; index < 3; index++)
            {
                using TcpClient connection = await listener.AcceptTcpClientAsync(timeout.Token);
                await using NetworkStream stream = connection.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var request = new StringBuilder();
                while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 } line) request.AppendLine(line);
                headers.Add(request.ToString());
                string response = index switch
                {
                    0 => "HTTP/1.1 302 Found\r\nLocation: /protected/next.mp4\r\nSet-Cookie: handshake=ready; Path=/protected\r\nContent-Length: 0\r\n",
                    1 => $"HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:{port}/final.mp4\r\nContent-Length: 0\r\n",
                    _ => "HTTP/1.1 200 OK\r\nContent-Type: video/mp4\r\nContent-Length: 3\r\n"
                };
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response + "Connection: close\r\n\r\n"), timeout.Token);
                if (index == 2) await stream.WriteAsync(new byte[] { 1, 2, 3 }, timeout.Token);
            }
        }
    }

    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private static string[] Parts(string header) => header.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static HttpResponseMessage Redirect(string location) => new(HttpStatusCode.Found)
    { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } };
    private static HttpResponseMessage Media() => new(HttpStatusCode.OK)
    { Content = new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new MediaTypeHeaderValue("video/mp4") } } };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        internal List<(string Cookies, Uri? Referrer)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Headers.TryGetValues("Cookie", out var values) ? string.Join("; ", values) : "", request.Headers.Referrer));
            HttpResponseMessage response = respond(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
