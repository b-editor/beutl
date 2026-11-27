using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace Beutl.Agents;

public sealed class AgentRpcException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}

public sealed class AgentWorkerDisconnectedException(string message, Exception? innerException = null)
    : IOException(message, innerException);

// Each peer owns a single writer. Requests are answered independently of the reader so an
// in-flight turn can receive an approval response or be interrupted on the same connection.
internal sealed class AgentJsonRpc(
    TextReader input,
    TextWriter output,
    Func<string, JsonElement?, CancellationToken, Task<JsonElement?>> handleRequest,
    Action<string, JsonElement?>? handleNotification = null)
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _incoming = new();
    private readonly Channel<string> _outgoing = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private long _nextId;
    private volatile bool _closed;

    internal bool IsConnected => !_closed;
    internal Task Completion { get; private set; } = Task.CompletedTask;
    internal AgentWorkerDisconnectedException DisconnectError { get; private set; } = new("The agent worker connection closed.");

    internal void Start() => Completion = RunAsync();

    internal void Notify(string method, JsonElement? parameters)
        => _outgoing.Writer.TryWrite(JsonSerializer.Serialize(new { jsonrpc = "2.0", method, @params = parameters }, AgentJson.Options));

    internal async Task<JsonElement> RequestAsync(string method, JsonElement? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsConnected)
            throw DisconnectError;

        long id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        try
        {
            if (!_outgoing.Writer.TryWrite(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters }, AgentJson.Options)))
                throw DisconnectError;

            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Notify("$/cancelRequest", AgentJson.ToElement(new { id }));
            throw;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task RunAsync()
    {
        var read = ReadAsync();
        var write = WriteAsync();
        try
        {
            await (await Task.WhenAny(read, write).ConfigureAwait(false)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DisconnectError = new AgentWorkerDisconnectedException("The agent worker connection failed.", ex);
        }
        finally
        {
            _closed = true;
            _outgoing.Writer.TryComplete();
            Task cancellation = _lifetime.CancelAsync();
            foreach (var request in _pending.Values)
            {
                request.TrySetException(DisconnectError);
            }

            try
            {
                await Task.WhenAll(read, write, cancellation).ConfigureAwait(false);
            }
            catch
            {
                // The first I/O failure is available through DisconnectError.
            }
            _lifetime.Dispose();
        }
    }

    private async Task ReadAsync()
    {
        while (await input.ReadLineAsync(_lifetime.Token).ConfigureAwait(false) is { } line)
        {
            using var document = JsonDocument.Parse(line);
            var frame = document.RootElement;
            if (frame.ValueKind != JsonValueKind.Object
                || !frame.TryGetProperty("jsonrpc", out var version) || version.GetString() != "2.0")
                throw new JsonException("Expected a JSON-RPC 2.0 frame.");

            bool hasId = frame.TryGetProperty("id", out var id);
            if (!frame.TryGetProperty("method", out var methodElement))
            {
                if (hasId && id.TryGetInt64(out long replyId) && _pending.TryGetValue(replyId, out var completion))
                {
                    if (frame.TryGetProperty("error", out var error))
                        completion.TrySetException(new AgentRpcException(error.GetProperty("code").GetInt32(), error.GetProperty("message").GetString()!));
                    else
                        completion.TrySetResult(frame.GetProperty("result").Clone());
                }
                continue;
            }

            string method = methodElement.GetString() ?? throw new JsonException("Expected a JSON-RPC method.");
            JsonElement? parameters = frame.TryGetProperty("params", out var value) ? value.Clone() : null;
            if (hasId)
            {
                var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                var requestId = id.Clone();
                if (!_incoming.TryAdd(requestId.GetRawText(), cancellation))
                {
                    cancellation.Dispose();
                    ReplyError(requestId, -32600, "Duplicate request ID.");
                    continue;
                }

                _ = ReplyAsync(requestId, method, parameters, cancellation);
            }
            else if (method == "$/cancelRequest" && parameters is { } cancel && cancel.TryGetProperty("id", out var target))
            {
                if (_incoming.TryGetValue(target.GetRawText(), out var cancellation))
                    _ = CancelAsync(cancellation);
            }
            else
            {
                handleNotification?.Invoke(method, parameters);
            }
        }
    }

    private static async Task CancelAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The matching request has already completed.
        }
    }

    private async Task ReplyAsync(JsonElement id, string method, JsonElement? parameters, CancellationTokenSource cancellation)
    {
        try
        {
            var result = await handleRequest(method, parameters, cancellation.Token).ConfigureAwait(false);
            _outgoing.Writer.TryWrite(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }, JsonSerializerOptions.Default));
        }
        catch (AgentRpcException ex)
        {
            ReplyError(id, ex.Code, ex.Message);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            ReplyError(id, -32800, "Request cancelled.");
        }
        catch (Exception ex)
        {
            ReplyError(id, -32603, ex.Message);
        }
        finally
        {
            _incoming.TryRemove(id.GetRawText(), out _);
            cancellation.Dispose();
        }
    }

    private void ReplyError(JsonElement id, int code, string message)
        => _outgoing.Writer.TryWrite(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, error = new { code, message } }));

    private async Task WriteAsync()
    {
        await foreach (string frame in _outgoing.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
        {
            await output.WriteLineAsync(frame.AsMemory(), _lifetime.Token).ConfigureAwait(false);
            await output.FlushAsync(_lifetime.Token).ConfigureAwait(false);
        }
    }
}
