using System.IO.Pipes;
using Beutl.FFmpegIpc.Protocol;

namespace Beutl.FFmpegIpc.Transport;

/// <summary>
/// 名前付きパイプ上のIPC接続。リクエスト/レスポンスの送受信を行う。
/// </summary>
public sealed class IpcConnection : IDisposable
{
    private readonly PipeStream _pipe;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _readLock = new(1, 1);
    private int _nextId;
    private bool _disposed;

    public IpcConnection(PipeStream pipe)
    {
        _pipe = pipe;
    }

    public bool IsConnected => _pipe.IsConnected;

    public int NextId() => Interlocked.Increment(ref _nextId);

    public async ValueTask SendAsync(IpcMessage message, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await MessageSerializer.WriteMessageAsync(_pipe, message, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public IpcMessage? Receive()
    {
        _readLock.Wait();
        try
        {
            return MessageSerializer.ReadMessage(_pipe);
        }
        finally
        {
            _readLock.Release();
        }
    }

    public async ValueTask<IpcMessage?> ReceiveAsync(CancellationToken ct = default)
    {
        await _readLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await MessageSerializer.ReadMessageAsync(_pipe, ct).ConfigureAwait(false);
        }
        finally
        {
            _readLock.Release();
        }
    }

    /// <summary>
    /// リクエストを送信し、レスポンスを待つ。送信→受信をatomicに行い、
    /// 並行アクセス時にレスポンスが混在しないことを保証する。
    /// </summary>
    public async ValueTask<IpcMessage> SendAndReceiveAsync(IpcMessage request, CancellationToken ct = default)
    {
        await _requestLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await MessageSerializer.WriteMessageAsync(_pipe, request, ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }

            await _readLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var response = await MessageSerializer.ReadMessageAsync(_pipe, ct).ConfigureAwait(false)
                    ?? throw new IOException("Connection closed while waiting for response.");

                if (response.Error != null)
                    throw new FFmpegWorkerException(response.Error, response.ErrorStackTrace);

                if (response.Id != request.Id)
                    throw new InvalidOperationException(
                        $"Response ID mismatch: expected {request.Id}, got {response.Id}");

                return response;
            }
            finally
            {
                _readLock.Release();
            }
        }
        finally
        {
            _requestLock.Release();
        }
    }

    /// <summary>
    /// ペイロード付きリクエストを送信し、レスポンスのペイロードをデシリアライズして返す。
    /// </summary>
    public async ValueTask<TResponse> RequestAsync<TRequest, TResponse>(
        MessageType requestType, MessageType responseType, TRequest payload, CancellationToken ct = default)
    {
        int id = NextId();
        var request = IpcMessage.Create(id, requestType, payload);
        var response = await SendAndReceiveAsync(request, ct).ConfigureAwait(false);

        if (response.Type == MessageType.Error)
            throw new FFmpegWorkerException(response.Error ?? "Unknown error", response.ErrorStackTrace);

        if (response.Type != responseType)
            throw new InvalidOperationException(
                $"Unexpected response type: expected {responseType}, got {response.Type}");

        return response.GetPayload<TResponse>()
            ?? throw new InvalidOperationException($"Failed to deserialize response payload for {responseType}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _requestLock.Dispose();
        _writeLock.Dispose();
        _readLock.Dispose();
        _pipe.Dispose();
    }
}
