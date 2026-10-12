using System.IO.Compression;
using System.Net.Sockets;
using System.Text;

using Beutl.Editor.Components.WebBrowserTab;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class BrowserContentEncodingTests
{
    [TestCase("gzip")]
    [TestCase("br")]
    [TestCase("deflate")]
    public async Task CompressedMediaIsSavedAsDecodedBytes(string encoding)
    {
        byte[] media = Enumerable.Range(0, 9000).Select(value => (byte)value).ToArray();
        var progress = new RecordingProgress();
        await WithResponseAsync(encoding, Compress(media, encoding), async (uri, directory, token) =>
        {
            string file = await BrowserMediaDownload.Default.DownloadAsync(uri, directory, null, progress, token);
            Assert.That(await File.ReadAllBytesAsync(file, token), Is.EqualTo(media));
            Assert.That(progress.Totals, Is.Not.Empty.And.All.Null);
        });
    }

    [TestCase("gzip")]
    [TestCase("br")]
    [TestCase("deflate")]
    public async Task CompressedHtmlIsRejectedAfterDecoding(string encoding)
    {
        byte[] html = Encoding.UTF8.GetBytes("<!doctype html><html><body>Please sign in</body></html>");
        await WithResponseAsync(encoding, Compress(html, encoding), async (uri, directory, token) =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => BrowserMediaDownload.Default.DownloadAsync(uri, directory, null, null, token));
            Assert.That(Directory.Exists(directory), Is.False);
        });
    }

    [TestCase("gzip, br")]
    [TestCase("zstd")]
    public async Task RemainingUnsupportedEncodingsAreNotPublishedAsMedia(string encoding)
    {
        byte[] body = encoding == "gzip, br" ? Compress(Compress([1, 2, 3], "gzip"), "br") : [1, 2, 3];
        await WithResponseAsync(encoding, body, async (uri, directory, token) =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => BrowserMediaDownload.Default.DownloadAsync(uri, directory, null, null, token));
            Assert.That(Directory.Exists(directory), Is.False);
        });
    }

    [Test]
    public async Task IdentityEncodingKeepsTheOriginalMedia()
    {
        await WithResponseAsync("identity", [1, 2, 3], async (uri, directory, token) =>
        {
            string file = await BrowserMediaDownload.Default.DownloadAsync(uri, directory, null, null, token);
            Assert.That(await File.ReadAllBytesAsync(file, token), Is.EqualTo(new byte[] { 1, 2, 3 }));
        });
    }

    private static byte[] Compress(byte[] bytes, string encoding)
    {
        using var buffer = new MemoryStream();
        using (Stream encoder = encoding switch
        {
            "gzip" => new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true),
            "br" => new BrotliStream(buffer, CompressionLevel.Fastest, leaveOpen: true),
            _ => new ZLibStream(buffer, CompressionLevel.Fastest, leaveOpen: true)
        }) encoder.Write(bytes);
        return buffer.ToArray();
    }

    private static async Task WithResponseAsync(string encoding, byte[] body, Func<Uri, string, CancellationToken, Task> check)
    {
        using var temporary = new TemporaryDirectory();
        await using var server = new LoopbackServer();
        var uri = new Uri($"http://127.0.0.1:{server.Port}/media.mp4");
        Task serving = server.Serve(ServeAsync);
        await check(uri, temporary.Path, server.Token);
        await serving;

        async Task ServeAsync(TcpListener listener, CancellationToken token)
        {
            using TcpClient connection = await listener.AcceptTcpClientAsync(token);
            await using NetworkStream stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            while (await reader.ReadLineAsync(token) is { Length: > 0 }) { }
            string headers = $"HTTP/1.1 200 OK\r\nContent-Type: video/mp4\r\nContent-Encoding: {encoding}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), token);
            await stream.WriteAsync(body, token);
        }
    }

    private sealed class RecordingProgress : IProgress<(long Received, long? Total)>
    {
        internal List<long?> Totals { get; } = [];
        public void Report((long Received, long? Total) value) => Totals.Add(value.Total);
    }
}
