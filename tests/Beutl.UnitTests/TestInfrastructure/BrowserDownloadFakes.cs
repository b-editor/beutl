using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;

namespace Beutl.UnitTests.TestInfrastructure;

// Cancels the download at its first progress report.
public sealed class CancelProgress(CancellationTokenSource cancellation) : IProgress<(long Received, long? Total)>
{
    public void Report((long Received, long? Total) value) => cancellation.Cancel();
}

// Answers every request with the body and Content-Type (none when mediaType is null). The body is read
// back at most 7 bytes at a time, so content inspection has to work across read boundaries.
public sealed class BodyHandler(byte[] body, string? mediaType, string? charset = null) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var content = new StreamContent(new SmallReadStream(body));
        if (mediaType != null)
            content.Headers.ContentType = new MediaTypeHeaderValue(mediaType) { CharSet = charset };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = content });
    }

    private sealed class SmallReadStream(byte[] body) : MemoryStream(body)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 7)], cancellationToken);
    }
}

// A TCP listener on a loopback port for tests that need the real HTTP transport. Its token cancels after
// 15 seconds so that a stuck exchange fails the test; Dispose cancels it, stops listening and waits for
// the task started by Serve.
public sealed class LoopbackServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(15));
    private Task _serving = Task.CompletedTask;

    public LoopbackServer()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public int Port { get; }

    public CancellationToken Token => _timeout.Token;

    public Task Serve(Func<TcpListener, CancellationToken, Task> serve) => _serving = serve(_listener, Token);

    public async ValueTask DisposeAsync()
    {
        _timeout.Cancel();
        _listener.Stop();
        try { await _serving; } catch (Exception) when (_timeout.IsCancellationRequested) { }
        _timeout.Dispose();
    }
}
