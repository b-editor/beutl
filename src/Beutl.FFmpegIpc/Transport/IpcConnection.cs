using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using Beutl.FFmpegIpc.Protocol;

namespace Beutl.FFmpegIpc.Transport;

/// <summary>
/// 名前付きパイプ上のIPC接続。リクエスト/レスポンスの送受信を行う。
/// 通常モード: SendAndReceiveAsync で逐次リクエスト/レスポンス。
/// 多重化モード: StartMultiplexedReceive でバックグラウンド受信ループを開始し、
///              IDベースで並行リクエスト/レスポンスを実現する。送信元のキャンセルと
///              レスポンス到着がレースしうるため、待機者を失ったレスポンスは
///              <see cref="DroppedResponseHandler"/> 経由でドレインされる。
/// </summary>
public sealed class IpcConnection : IDisposable
{
    private readonly PipeStream _pipe;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _readLock = new(1, 1);
    private int _nextId;
    private volatile bool _disposed;

    // 多重化モード用
    private readonly ConcurrentDictionary<int, TaskCompletionSource<IpcMessage>> _pendingRequests = new();
    private Task? _receiveLoopTask;
    private CancellationTokenSource? _receiveLoopCts;
    // 受信ループが I/O 障害/プロトコル破損で終了した際の例外。
    // 死後に届いたリクエストを永久 await させないため、SendAndReceiveMultiplexedAsync で
    // fail-fast に使う。Volatile.Write/Read で公開する。
    private Exception? _receiveLoopFault;

    public IpcConnection(PipeStream pipe)
    {
        _pipe = pipe;
    }

    public bool IsConnected => _pipe.IsConnected;

    public bool IsMultiplexed => _receiveLoopTask != null;

    /// <summary>
    /// 多重化モードでルーティング先を失ったレスポンスを受け取るフック。
    /// 次のいずれかで呼ばれる:
    ///   - <c>_pendingRequests</c> から TCS を取り出せたがキャンセル済みで TrySetResult が失敗した。
    ///   - 対応する <c>request.Id</c> の TCS が <c>_pendingRequests</c> に居なかった (キャンセル後到着の stray)。
    /// 想定する使い方は、共有メモリバッファ (bufferIndex 等) を握ったまま捨てられるレスポンスを
    /// 上位レイヤーで解放するためのコールバック。現状は配線されておらず、配線するのは消費側 (例:
    /// IpcFrameProvider) の責務とする。
    /// 呼び出しは受信ループスレッドで同期的に行われるため、ブロックすると後続メッセージの
    /// 処理が遅延する。例外は投げないことを契約とするが、万一スローした場合でも受信ループは
    /// 継続し、内容は <see cref="System.Diagnostics.Trace.TraceError(string)"/> に記録される。
    /// </summary>
    public Action<IpcMessage>? DroppedResponseHandler { get; set; }

    public int NextId() => Interlocked.Increment(ref _nextId);

    /// <summary>
    /// 多重化モード用 pending request 辞書のエントリ数。テスト/診断専用。
    /// </summary>
    internal int PendingRequestCount => _pendingRequests.Count;

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
            // 受信ループが終了する理由を以下のように区別する:
            //   - loopCt キャンセル/ObjectDisposedException: 自プロセス由来の停止。
            //     finally で待機中 TCS を TrySetCanceled(loopCt) してキャンセル扱い。
            //   - EOF (msg == null) / IOException: 相手側起因の切断。
            //     terminationError に詰めて TrySetException で伝播し、
            //     「ユーザーがキャンセル」と「接続が死んだ」を呼び出し元から区別可能にする。
            //   - 想定外例外 (プロトコル破損/JSON 失敗 など): 同じ termination 経路に集約し、
            //     IOException として待機中 TCS と _receiveLoopFault に伝播する。
            Exception? terminationError = null;
            try
            {
                while (!loopCt.IsCancellationRequested)
                {
                    var msg = await MessageSerializer.ReadMessageAsync(_pipe, loopCt).ConfigureAwait(false);
                    if (msg == null)
                    {
                        terminationError = new IOException(
                            "IPC receive loop terminated: remote endpoint closed the pipe.");
                        break;
                    }

                    if (_pendingRequests.TryRemove(msg.Id, out var tcs))
                    {
                        // tcs が既にキャンセル済みなら TrySetResult は false を返す。
                        // その場合、呼び出し元はレスポンスを受け取らないので
                        // 上位レイヤーに通知してリソース解放のチャンスを与える。
                        if (!tcs.TrySetResult(msg))
                        {
                            InvokeDroppedResponseHandler(msg);
                        }
                    }
                    else
                    {
                        // 待機中の TCS が居ない (キャンセル後に到着した stray message)。
                        InvokeDroppedResponseHandler(msg);
                    }
                }
            }
            catch (OperationCanceledException) when (loopCt.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (loopCt.IsCancellationRequested) { }
            catch (IOException ex)
            {
                terminationError = new IOException(
                    "IPC receive loop terminated due to a broken pipe. The remote endpoint may have crashed.", ex);
            }
            catch (Exception ex)
            {
                // プロトコル破損 (length out of range, JSON deserialize 失敗, 想定外の状態) など
                // I/O 以外の例外も同じ termination 経路に集約し、呼び出し元から
                // 「キャンセル」と「ループが死んだ」を区別可能にする。
                terminationError = new IOException(
                    "IPC receive loop terminated due to an unexpected protocol or deserialization error.", ex);
            }
            finally
            {
                // 死後の新規呼び出しが fail-fast できるよう、終了状態を先に公開してから
                // 待機中リクエストを解放する。逆順だと finally の foreach 後に追加された
                // TCS をループが拾えず永久ハングしうる。SendAndReceiveMultiplexedAsync 側で
                // _pendingRequests への登録後にもう一度 fault を読むことで競合を閉じる。
                // I/O / プロトコル系の終端は terminationError、loopCt キャンセルや
                // 自プロセス Dispose は ObjectDisposedException として記録する。後者を
                // 「fault は無し」と表現すると、ループ死亡後の caller が永久 await しうる。
                Volatile.Write(
                    ref _receiveLoopFault,
                    terminationError ?? (Exception)new ObjectDisposedException(
                        nameof(IpcConnection),
                        "IPC receive loop has stopped; the connection no longer accepts multiplexed requests."));

                // 残っている全ての待機中リクエストを解放。I/O 障害があれば例外として、
                // それ以外 (ObjectDisposed / loopCt キャンセル) は loopCt 付きでキャンセル。
                // EOF は terminationError 経路を通るのでここでは TrySetException 側に入る点に注意。
                foreach (var kvp in _pendingRequests)
                {
                    if (_pendingRequests.TryRemove(kvp.Key, out var tcs))
                    {
                        if (terminationError != null)
                            tcs.TrySetException(terminationError);
                        else
                            tcs.TrySetCanceled(loopCt);
                    }
                }
            }
        }, CancellationToken.None);
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
        ct.ThrowIfCancellationRequested();

        // 受信ループが既に死んでいたら、TCS をハングさせずに即時失敗する。
        var fault = Volatile.Read(ref _receiveLoopFault);
        if (fault != null)
            throw fault;

        var tcs = new TaskCompletionSource<IpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[request.Id] = tcs;

        // 登録→fault チェックの順を逆にできない: 逆だと「fault 読み (null)」→「ループが
        // finally で fault 書き&foreach (空)」→「登録」のレースで永久 await になる。
        // 登録後にもう一度読み直すことで、foreach に拾われなかった TCS も検出する。
        fault = Volatile.Read(ref _receiveLoopFault);
        if (fault != null)
        {
            _pendingRequests.TryRemove(new KeyValuePair<int, TaskCompletionSource<IpcMessage>>(request.Id, tcs));
            throw fault;
        }

        try
        {
            // SendAsync の前にキャンセルを購読し、送信中・受信待機中いずれの局面でも
            // ct 発火が確実に tcs へ伝播するようにする。
            // 旧実装は SendAsync 完了後に Register していたため、送信成功とキャンセル発火と
            // レスポンス到着が同時に走ると、TCS が中途半端に解決されず競合の隙間で
            // バッファインデックスを未消費のまま落とすケースがあった。
            // Register 自体は ObjectDisposedException 等で失敗しうるため try の内側
            // に置き、失敗時にも finally で pending を確実に撤去する。
            using var registration = ct.Register(static state =>
            {
                var (innerTcs, token) = ((TaskCompletionSource<IpcMessage> Tcs, CancellationToken Token))state!;
                innerTcs.TrySetCanceled(token);
            }, (tcs, ct));

            await SendAsync(request, ct).ConfigureAwait(false);
            var response = await tcs.Task.ConfigureAwait(false);

            if (response.Error != null)
                throw new FFmpegWorkerException(response.Error, response.ErrorStackTrace);

            return response;
        }
        finally
        {
            // 自身が登録した TCS のみを撤去する (KeyValuePair オーバーロード)。
            // 主目的は受信ループとのレース対策: ループが先にこのスロットを取り出して
            // 別レスポンスを刺し直していた場合、参照不一致になり TryRemove は失敗 — 別リクエストの
            // TCS を取り違えて消すのを防ぐ。副次的に、_nextId が int を一巡 (2^31 件) して Id が
            // 再利用された場合の保険も兼ねる。
            _pendingRequests.TryRemove(new KeyValuePair<int, TaskCompletionSource<IpcMessage>>(request.Id, tcs));
        }
    }

    private void InvokeDroppedResponseHandler(IpcMessage message)
    {
        var handler = DroppedResponseHandler;
        if (handler == null) return;
        try
        {
            handler(message);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException
                                       and not StackOverflowException
                                       and not AccessViolationException)
        {
            // ハンドラ例外は受信ループを止めない。診断のため Trace へ送る。
            Trace.TraceError(
                $"IpcConnection.DroppedResponseHandler threw {ex.GetType().Name} for message Id={message.Id} Type={message.Type}: {ex}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _receiveLoopCts?.Cancel();

        // 受信ループを待ってからパイプ/セマフォを破棄する。先にパイプを破棄すると
        // 進行中の ReadMessageAsync が ObjectDisposedException で死に、自己 Dispose が
        // 誤って "broken pipe" として pending リクエストに伝播してしまう。上限を切って
        // 待つことで、ループ内ハング (理論上ありえない) でも Dispose は必ず進む。
        try
        {
            if (_receiveLoopTask is { } task && !task.Wait(TimeSpan.FromSeconds(5)))
            {
                // タイムアウト = ループが停止しなかった。続行するとパイプ破棄で
                // ObjectDisposedException を引き起こし、その先で診断が消える。
                // 続行はするが、痕跡が残るよう Trace に出す。
                Trace.TraceError("IpcConnection.Dispose: receive loop did not exit within 5s; proceeding with disposal.");
            }
        }
        catch (AggregateException ex)
        {
            // 受信ループ内例外は finally で pending TCS / _receiveLoopFault へ伝播済みだが、
            // Dispose 時の根因をたどれるよう原例外を残す。
            Trace.TraceError($"IpcConnection.Dispose: receive loop ended with exception: {ex}");
        }

        _receiveLoopCts?.Dispose();

        _requestLock.Dispose();
        _writeLock.Dispose();
        _readLock.Dispose();
        _pipe.Dispose();
    }
}
