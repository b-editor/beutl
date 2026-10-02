using System.Net;
using System.Text.Json;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class HostedGitLfsTransferAgentTests
{
    private const long ObjectSize = 5L * 1024 * 1024 * 1024 + 1;
    private const int PartSize = 64 * 1024 * 1024;
    private static readonly string s_oid = new('a', 64);

    [Test]
    public async Task ResumesAboveFiveGiBAndRetriesAStreamedPart()
    {
        using var handler = new MultipartHandler();
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var input = new StringReader(string.Join("\n",
            JsonSerializer.Serialize(new { @event = "init", operation = "upload" }),
            JsonSerializer.Serialize(new
            {
                @event = "upload", oid = s_oid, size = ObjectSize, path = "virtual-large-media",
                action = new
                {
                    href = $"https://beutl.example/api/v3/git/repo.git/info/lfs/objects/{s_oid}/multipart",
                    header = new Dictionary<string, string> { ["Authorization"] = "Bearer temporary" }
                }
            }),
            JsonSerializer.Serialize(new { @event = "terminate" })));
        using var output = new StringWriter();

        int exitCode = await HostedGitLfsTransferAgent.RunAsync(
            input, output, http, CancellationToken.None,
            static _ => ObjectSize, static _ => new VirtualFile(ObjectSize));

        Assert.That(exitCode, Is.Zero, output.ToString());
        Assert.That(handler.Part80Attempts, Is.EqualTo(2));
        Assert.That(handler.UploadedParts, Is.EqualTo(new[] { (80, (long)PartSize), (81, 1L) }));
        Assert.That(handler.MaxReadChunk, Is.LessThanOrEqualTo(128 * 1024));
        Assert.That(handler.SawAuthorization, Is.True);
        using JsonDocument last = JsonDocument.Parse(output.ToString().Trim().Split('\n')[^1]);
        Assert.That(last.RootElement.GetProperty("event").GetString(), Is.EqualTo("complete"));
        Assert.That(last.RootElement.GetProperty("oid").GetString(), Is.EqualTo(s_oid));
        Assert.That(last.RootElement.TryGetProperty("error", out _), Is.False);
    }

    private sealed class MultipartHandler : HttpMessageHandler
    {
        public int Part80Attempts { get; private set; }
        public int MaxReadChunk { get; private set; }
        public bool SawAuthorization { get; private set; }
        public List<(int Number, long Size)> UploadedParts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SawAuthorization |= request.Headers.Authorization?.ToString() == "Bearer temporary";
            string path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.EndsWith("/multipart", StringComparison.Ordinal))
            {
                return Json(new
                {
                    complete = false, partSize = PartSize, partCount = 81,
                    parts = Enumerable.Range(1, 79)
                        .Select(number => new { partNumber = number, etag = $"etag-{number}", size = PartSize })
                });
            }
            if (request.Method == HttpMethod.Put && path.Contains("/parts/", StringComparison.Ordinal))
            {
                int number = int.Parse(path[(path.LastIndexOf('/') + 1)..]);
                if (number == 80 && ++Part80Attempts == 1)
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

                long size = 0;
                byte[] buffer = new byte[128 * 1024];
                await using Stream stream = await request.Content!.ReadAsStreamAsync(cancellationToken);
                int read;
                while ((read = await stream.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    MaxReadChunk = Math.Max(MaxReadChunk, read);
                    size += read;
                }
                Assert.That(request.Content.Headers.ContentLength, Is.EqualTo(size));
                UploadedParts.Add((number, size));
                return Json(new { partNumber = number, etag = $"etag-{number}", size });
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/complete", StringComparison.Ordinal))
                return Json(new { oid = s_oid, size = ObjectSize });
            throw new InvalidOperationException($"Unexpected LFS request: {request.Method} {path}");
        }

        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value))
        };
    }

    private sealed class VirtualFile(long length) : Stream
    {
        private long _position;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => _position = value; }
        public override long Seek(long offset, SeekOrigin origin) =>
            _position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = (int)Math.Min(count, length - _position);
            Array.Clear(buffer, offset, read);
            _position += read;
            return read;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = (int)Math.Min(buffer.Length, length - _position);
            buffer.Span[..read].Clear();
            _position += read;
            return ValueTask.FromResult(read);
        }
        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
