using System.Collections.Concurrent;
using System.IO.Pipes;
using Beutl.FFmpegIpc.Protocol;

namespace Beutl.FFmpegIpc.Transport;

/// <summary>
/// 名前付きパイプ上のIPC接続。リクエスト/レスポンスの送受信を行う。
/// 通常モード: SendAndReceiveAsync で逐次リクエスト/レスポンス。
/// 多重化モード: StartMultiplexedReceive でバックグラウンド受信ループを開始し、
///              IDベースで並行リクエスト/レスポンスを実現する。
/// </summary>
public sealed class IpcConnection : IDisposable
{
    private readonly PipeStream _pipe;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _readLock = new(1, 1);
    private int _nextId;
    private bool _disposed;

    // 多重化モード用
    private readonly ConcurrentDictionary<int, TaskCompletionSource<IpcMessage>> _pendingRequests = new();
    private Task? _receiveLoopTask;
    private CancellationTokenSource? _receiveLoopCts;

    public IpcConnection(PipeStream pipe)
    {
        _pipe = pipe;
    }

    public bool IsConnected => _pipe.IsConnected;

    public bool IsMultiplexed => _receiveLoopTask != null;

    public int NextId() => Interlocked.Increment(ref _nextId);

    /// <summary>
    /// 多重化受信モードを開始する。バックグラウンドでメッセージを受信し、
    /// IDベースで待機中のリクエストにルーティングする。
    /// クライアント側で複数リーダーからの並行リクエストを可能にする。
    /// </summary>
    public void StartMultiplexedReceive(CancellationToken ct = default)
    {
        if (_receiveLoopTask != null)
            throw new InvalidOperationException("Multiplexed receive already started.");

        _receiveLoopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var loopCt = _receiveLoopCts.Token;

        _receiveLoopTask = Task.Run(async () =>
        {
            try
            {
                while (!loopCt.IsCancellationRequested)
                {
                    var msg = await MessageSerializer.ReadMessageAsync(_pipe, loopCt).ConfigureAwait(false);
                    if (msg == null)
                        break;

                    if (_pendingRequests.TryRemove(msg.Id, out var tcs))
                    {
                        tcs.TrySetResult(msg);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            finally
            {
                // 残っている全ての待機中リクエストをキャンセル
                foreach (var kvp in _pendingRequests)
                {
                    if (_pendingRequests.TryRemove(kvp.Key, out var tcs))
                        tcs.TrySetCanceled();
                }
            }
        }, loopCt);
    }

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
    /// リクエストを送信し、レスポンスを待つ。
    /// 多重化モードではIDベースルーティングを使い、並行リクエストを可能にする。
    /// 通常モードでは送信→受信をatomicに行い、レスポンスの混在を防ぐ。
    /// </summary>
    public async ValueTask<IpcMessage> SendAndReceiveAsync(IpcMessage request, CancellationToken ct = default)
    {
        if (IsMultiplexed)
            return await SendAndReceiveMultiplexedAsync(request, ct).ConfigureAwait(false);

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

    private async ValueTask<IpcMessage> SendAndReceiveMultiplexedAsync(IpcMessage request, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<IpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[request.Id] = tcs;

        try
        {
            await SendAsync(request, ct).ConfigureAwait(false);
            using var registration = ct.Register(() => tcs.TrySetCanceled(ct));
            var response = await tcs.Task.ConfigureAwait(false);

            if (response.Error != null)
                throw new FFmpegWorkerException(response.Error, response.ErrorStackTrace);

            return response;
        }
        catch
        {
            _pendingRequests.TryRemove(request.Id, out _);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _receiveLoopCts?.Cancel();
        _receiveLoopCts?.Dispose();

        _requestLock.Dispose();
        _writeLock.Dispose();
        _readLock.Dispose();
        _pipe.Dispose();
    }
}
