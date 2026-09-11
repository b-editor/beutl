using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

using Beutl.Editor.Components.WebBrowserTab;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class BrowserMediaPreambleTests
{
    [TestCase("utf8")]
    [TestCase("utf8-no-bom")]
    [TestCase("utf16le")]
    [TestCase("utf16be")]
    [TestCase("utf32le")]
    [TestCase("utf32be")]
    public async Task SvgWithLongCommentsIsPreservedAcrossSmallByteReads(string encoding)
    {
        Encoding codec = GetEncoding(encoding);
        byte[] body = [.. codec.GetPreamble(), .. codec.GetBytes("<?xml version='1.0'?><!--" + new string('界', 7000)
            + "--><svg xmlns='http://www.w3.org/2000/svg'/>")];
        await AssertSvgIsPreserved(body);
    }

    [TestCase(4088)]
    [TestCase(4089)]
    [TestCase(4090)]
    public async Task SvgCommentEndingAtTheInspectionBoundaryIsNotHtml(int padding)
    {
        await AssertSvgIsPreserved(Encoding.UTF8.GetBytes("<!--" + new string('x', padding)
            + "--><svg xmlns='http://www.w3.org/2000/svg'/>"));
    }

    [TestCase("comment")]
    [TestCase("declaration")]
    [TestCase("whitespace")]
    public void HtmlAfterLongPreamblesIsRejectedWithoutPublishingAFile(string kind)
    {
        string preamble = kind switch
        {
            "comment" => "<!--" + new string('x', 9000) + "-->",
            "declaration" => "<?xml version='1.0' " + new string(' ', 9000) + "?>",
            _ => new string(' ', 9000)
        };
        string directory = NewDirectory();
        using var client = new HttpClient(new BodyHandler(Encoding.UTF8.GetBytes(preamble + "<!doctype html><html>Login</html>")));
        try
        {
            Assert.ThrowsAsync<InvalidOperationException>(() => new BrowserMediaDownload(client).DownloadAsync(
                new Uri("https://example.com/image.svg"), directory, null, null, default));
            Assert.That(Directory.Exists(directory) ? Directory.GetFiles(directory) : [], Is.Empty);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public void CancellationWhileReadingThePreambleRemovesThePartialFile()
    {
        string directory = NewDirectory();
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new BodyHandler(Encoding.UTF8.GetBytes("<!--" + new string('x', 9000)
            + "--><svg xmlns='http://www.w3.org/2000/svg'/>")));
        try
        {
            Assert.CatchAsync<OperationCanceledException>(() => new BrowserMediaDownload(client).DownloadAsync(
                new Uri("https://example.com/image.svg"), directory, null, new CancelProgress(cancellation), cancellation.Token));
            Assert.That(Directory.Exists(directory) ? Directory.GetFiles(directory) : [], Is.Empty);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static async Task AssertSvgIsPreserved(byte[] body)
    {
        string directory = NewDirectory();
        using var client = new HttpClient(new BodyHandler(body));
        try
        {
            string file = await new BrowserMediaDownload(client).DownloadAsync(new Uri("https://example.com/image.svg"),
                directory, null, null, default);
            Assert.That(await File.ReadAllBytesAsync(file), Is.EqualTo(body));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private static Encoding GetEncoding(string name) => name switch
    {
        "utf8" => new UTF8Encoding(true),
        "utf16le" => Encoding.Unicode,
        "utf16be" => Encoding.BigEndianUnicode,
        "utf32le" => Encoding.UTF32,
        "utf32be" => new UTF32Encoding(true, true),
        _ => new UTF8Encoding(false)
    };

    private sealed class BodyHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StreamContent(new SmallReadStream(body))
                { Headers = { ContentType = new MediaTypeHeaderValue("image/svg+xml") } }
            });
    }

    private sealed class SmallReadStream(byte[] body) : MemoryStream(body)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 7)], cancellationToken);
    }

    private sealed class CancelProgress(CancellationTokenSource cancellation) : IProgress<(long Received, long? Total)>
    {
        public void Report((long Received, long? Total) value) => cancellation.Cancel();
    }
}
