using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Transport;

namespace Beutl.FFmpegIpc.Tests;

/// <summary>
/// IpcConnection.SendAndReceiveMultiplexedAsync のキャンセル順序・競合パス・
/// ドロップ応答フック・受信ループ終端処理を検証する。
/// NamedPipe を使ったクロスプラットフォーム動作。
/// </summary>
/// <remarks>
/// NamedPipe をテストごとに多数生成しタイミングに依存するため、
/// 他フィクスチャと並列に動作させない。
/// </remarks>
[TestFixture, NonParallelizable]
public class IpcConnectionMultiplexedTests
{
    private static readonly MessageType RequestType = MessageType.CloseReader;
    private static readonly MessageType ResponseType = MessageType.CloseReaderResult;

    private static string NewPipeName() => "beutl-ipc-" + Guid.NewGuid().ToString("N")[..8];

    private static (NamedPipeServerStream Server, NamedPipeClientStream Client) ConnectPair()
    {
        string name = NewPipeName();
        var server = new NamedPipeServerStream(
            name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var client = new NamedPipeClientStream(
            ".", name, PipeDirection.InOut, PipeOptions.Asynchronous);

        var serverConnectTask = server.WaitForConnectionAsync();
        client.Connect(TimeSpan.FromSeconds(5));
        serverConnectTask.GetAwaiter().GetResult();
        return (server, client);
    }

    private static int PendingCount(IpcConnection conn) => conn.PendingRequestCount;

    [Test]
    public async Task ConcurrentRequests_AllReceiveCorrectResponses()
    {
        var (server, client) = ConnectPair();
        using var conn = new IpcConnection(client);
        conn.StartMultiplexedReceive();

        // Worker 役: 5 本溜まったら受信と逆順でレスポンスを返す。
        // 受信順とは異なる順序で返すことで ID ルーティングを実際にストレスする。
        var workerCts = new CancellationTokenSource();
        var workerTask = Task.Run(async () =>
        {
            var received = new List<IpcMessage>();
            while (!workerCts.IsCancellationRequested)
            {
                var req = await MessageSerializer.ReadMessageAsync(server, workerCts.Token);
                if (req == null) return;
                received.Add(req);
                if (received.Count == 5)
                {
                    foreach (var msg in received.AsEnumerable().Reverse())
                    {
                        var resp = IpcMessage.CreateSimple(msg.Id, ResponseType);
                        await MessageSerializer.WriteMessageAsync(server, resp, workerCts.Token);
                    }
                    return;
                }
            }
        });

        try
        {
            var tasks = new List<(int Id, Task<IpcMessage> Task)>();
            for (int i = 0; i < 5; i++)
            {
                int id = conn.NextId();
                var req = IpcMessage.CreateSimple(id, RequestType);
                tasks.Add((id, conn.SendAndReceiveAsync(req).AsTask()));
            }

            await AwaitWithTimeout(Task.WhenAll(tasks.Select(t => t.Task)), TimeSpan.FromSeconds(10), "all 5 concurrent requests complete");
            foreach (var (id, t) in tasks)
            {
                Assert.That(t.Result.Id, Is.EqualTo(id));
                Assert.That(t.Result.Type, Is.EqualTo(ResponseType));
            }

            Assert.That(PendingCount(conn), Is.EqualTo(0));
        }
        finally
        {
            workerCts.Cancel();
            server.Dispose();
            try { await workerTask; }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
    }

    [Test]
    public void AlreadyCanceledToken_ThrowsBeforeRegistering()
    {
        var (server, client) = ConnectPair();
        using var _ = server;
        using var conn = new IpcConnection(client);
        conn.StartMultiplexedReceive();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        int id = conn.NextId();
        var req = IpcMessage.CreateSimple(id, RequestType);
        // ct.ThrowIfCancellationRequested が _pendingRequests への登録より先に走る。
        Assert.CatchAsync<OperationCanceledException>(
            async () => await conn.SendAndReceiveAsync(req, cts.Token));

        Assert.That(PendingCount(conn), Is.EqualTo(0));
    }

    [Test]
    public async Task CancelWhileAwaitingResponse_ThrowsAndDictIsClean()
    {
        var (server, client) = ConnectPair();
        using var conn = new IpcConnection(client);
        conn.StartMultiplexedReceive();

        // worker は意図的に返事しない。
        using var cts = new CancellationTokenSource();
        try
        {
            int id = conn.NextId();
            var req = IpcMessage.CreateSimple(id, RequestType);
            var requestTask = conn.SendAndReceiveAsync(req, cts.Token).AsTask();

            // 送信完了 (= pending に積まれた) を観測してからキャンセル。
            // 固定 Task.Delay だと CI 環境差でフレーキーになるため、PendingRequestCount を直接ポーリング。
            await WaitUntil(() => PendingCount(conn) == 1, TimeSpan.FromSeconds(5), "request enters pending dict");
            cts.Cancel();

            var oce = Assert.CatchAsync<OperationCanceledException>(async () => await requestTask);
            Assert.That(oce!.CancellationToken, Is.EqualTo(cts.Token));

            await WaitUntil(() => PendingCount(conn) == 0, TimeSpan.FromSeconds(5), "pending dict drains after cancel");
            Assert.That(PendingCount(conn), Is.EqualTo(0));
        }
        finally
        {
            server.Dispose();
        }
    }

    [Test]
    public async Task LateResponseAfterCancel_InvokesDroppedHandler_NoDeadlock()
    {
        var (server, client) = ConnectPair();
        using var conn = new IpcConnection(client);

        var dropped = new ConcurrentQueue<IpcMessage>();
        conn.DroppedResponseHandler = msg => dropped.Enqueue(msg);

        conn.StartMultiplexedReceive();

        using var cts = new CancellationTokenSource();
        int id;
        try
        {
            id = conn.NextId();
            var req = IpcMessage.CreateSimple(id, RequestType);
            var requestTask = conn.SendAndReceiveAsync(req, cts.Token).AsTask();

            await WaitUntil(() => PendingCount(conn) == 1, TimeSpan.FromSeconds(5), "request enters pending dict");
            cts.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await requestTask);
            await WaitUntil(() => PendingCount(conn) == 0, TimeSpan.FromSeconds(5), "pending dict drains after cancel");

            // 遅延レスポンスを送り込む → dropped ハンドラに来るはず。
            // cts は既に発火済みなので渡せるトークンが無く、無トークン書き込みでよい。
            var resp = IpcMessage.CreateSimple(id, ResponseType);
            await MessageSerializer.WriteMessageAsync(server, resp);

            await WaitUntil(() => dropped.Count >= 1, TimeSpan.FromSeconds(5), "DroppedResponseHandler is invoked for late response");
            Assert.That(dropped.Count, Is.EqualTo(1));
            Assert.That(dropped.TryDequeue(out var dropMsg), Is.True);
            Assert.That(dropMsg!.Id, Is.EqualTo(id));
            Assert.That(dropMsg.Type, Is.EqualTo(ResponseType));
        }
        finally
        {
            server.Dispose();
        }
    }

    [Test]
    public async Task DroppedResponseHandler_Throws_ReceiveLoopSurvives()
    {
        // DroppedResponseHandler が例外を投げても受信ループは生存し、
        // 後続リクエストが正常応答を受け取れる契約。
        var (server, client) = ConnectPair();
        using var _ = server;
        using var conn = new IpcConnection(client);

        int handlerCalls = 0;
        conn.DroppedResponseHandler = _ =>
        {
            Interlocked.Increment(ref handlerCalls);
            throw new InvalidOperationException("intentional from test");
        };
        conn.StartMultiplexedReceive();

        // 1) キャンセル後の遅延応答でハンドラを発火させる。
        using (var cts = new CancellationTokenSource())
        {
            int firstId = conn.NextId();
            var firstReq = IpcMessage.CreateSimple(firstId, RequestType);
            var firstTask = conn.SendAndReceiveAsync(firstReq, cts.Token).AsTask();

            // Drain the outbound request so the send completes before we cancel it: on Windows an
            // unread pipe write does not return, so without the drain whether the request was delivered
            // is platform-dependent.
            var drainedFirst = await AwaitWithResult(
                MessageSerializer.ReadMessageAsync(server).AsTask(), TimeSpan.FromSeconds(5));
            Assert.That(drainedFirst!.Id, Is.EqualTo(firstId));
            await WaitUntil(() => PendingCount(conn) == 1, TimeSpan.FromSeconds(5), "first request awaits its response");
            cts.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await firstTask);
            await WaitUntil(() => PendingCount(conn) == 0, TimeSpan.FromSeconds(5), "pending dict drains after cancel");

            var lateResp = IpcMessage.CreateSimple(firstId, ResponseType);
            await MessageSerializer.WriteMessageAsync(server, lateResp);
            await WaitUntil(() => Volatile.Read(ref handlerCalls) >= 1, TimeSpan.FromSeconds(5), "dropped handler is invoked");
        }

        // 2) 二本目のリクエストが正常応答を受け取る = 受信ループ生存中。
        int secondId = conn.NextId();
        var secondReq = IpcMessage.CreateSimple(secondId, RequestType);
        var secondTask = conn.SendAndReceiveAsync(secondReq).AsTask();

        // Drain the outbound request so the send completes and secondTask parks on its response TCS;
        // otherwise secondTask stalls in the send and never completes even after the response is written.
        var drainedSecond = await AwaitWithResult(
            MessageSerializer.ReadMessageAsync(server).AsTask(), TimeSpan.FromSeconds(5));
        Assert.That(drainedSecond!.Id, Is.EqualTo(secondId));
        await WaitUntil(() => PendingCount(conn) == 1, TimeSpan.FromSeconds(5), "second request awaits its response");

        // ハンドラを差し替えてから応答を返す (二本目で再度発火させない)。
        conn.DroppedResponseHandler = null;
        var secondResp = IpcMessage.CreateSimple(secondId, ResponseType);
        await MessageSerializer.WriteMessageAsync(server, secondResp);

        var got = await AwaitWithResult(secondTask, TimeSpan.FromSeconds(5));
        Assert.That(got.Id, Is.EqualTo(secondId));
        Assert.That(got.Type, Is.EqualTo(ResponseType));
    }

    [Test]
    public async Task RaceCancelAndResponse_NoLeak_StressLoop()
    {
        // レスポンス勝ち / キャンセル勝ち どちらでもリークしない不変を確認。
        // 結果は気にしないが、dropped に乗ったメッセージは必ず送信した id と一致するはず。
        const int iterations = 500;

        for (int iter = 0; iter < iterations; iter++)
        {
            var (server, client) = ConnectPair();
            using var conn = new IpcConnection(client);

            var dropped = new ConcurrentQueue<IpcMessage>();
            conn.DroppedResponseHandler = msg => dropped.Enqueue(msg);
            conn.StartMultiplexedReceive();

            var workerCts = new CancellationTokenSource();
            var workerTask = Task.Run(async () =>
            {
                var req = await MessageSerializer.ReadMessageAsync(server, workerCts.Token);
                if (req == null) return;
                var resp = IpcMessage.CreateSimple(req.Id, ResponseType);
                await MessageSerializer.WriteMessageAsync(server, resp, workerCts.Token);
            });

            using var cts = new CancellationTokenSource();
            int id = conn.NextId();
            try
            {
                var req = IpcMessage.CreateSimple(id, RequestType);
                var requestTask = conn.SendAndReceiveAsync(req, cts.Token).AsTask();

                // 受信ループとキャンセルがほぼ同時に走るよう仕向ける。
                cts.Cancel();

                // キャンセル経路がリークすると await が返らなくなるため上限を切る。
                await AwaitWithTimeout(requestTask, TimeSpan.FromSeconds(5), $"iter={iter}: requestTask");
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            finally
            {
                workerCts.Cancel();
                server.Dispose();
                // worker は書き込み中に server.Dispose() を踏むと
                // ObjectDisposedException を投げうる。stress テストでは
                // この競合は不可避なので想定例外として許容する。
                try { await AwaitWithTimeout(workerTask, TimeSpan.FromSeconds(5), $"iter={iter}: workerTask"); }
                catch (OperationCanceledException) { }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
            }

            await WaitUntil(() => PendingCount(conn) == 0, TimeSpan.FromSeconds(2), $"iter={iter}: pending dict drains");
            Assert.That(PendingCount(conn), Is.EqualTo(0), $"iter={iter}");

            // dropped に乗ったメッセージはすべて送信した id と一致するはず。
            foreach (var d in dropped)
            {
                Assert.That(d.Id, Is.EqualTo(id), $"iter={iter}: dropped foreign id");
            }
        }
    }

    [Test]
    public async Task SamePendingRequestId_ThrowsAndFirstSurvives()
    {
        // 同じ Id の二重登録は 2 つ目だけが InvalidOperationException で失敗し、
        // 1 つ目の TCS には影響しない。
        var (server, client) = ConnectPair();
        using var _ = server;
        using var conn = new IpcConnection(client);
        conn.StartMultiplexedReceive();

        using var cts = new CancellationTokenSource();
        const int fixedId = 12345;
        var first = IpcMessage.CreateSimple(fixedId, RequestType);
        var second = IpcMessage.CreateSimple(fixedId, RequestType);

        var firstTask = conn.SendAndReceiveAsync(first, cts.Token).AsTask();
        await WaitUntil(() => PendingCount(conn) == 1, TimeSpan.FromSeconds(5), "first request enters pending dict");

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await conn.SendAndReceiveAsync(second));
        Assert.That(ex!.Message, Does.Contain("already in flight"));
        Assert.That(PendingCount(conn), Is.EqualTo(1));

        cts.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await firstTask);
        await WaitUntil(() => PendingCount(conn) == 0, TimeSpan.FromSeconds(5), "pending dict drains after cancel");
    }

    [Test]
    public async Task PostDispose_SendAndReceive_FailsFastWithObjectDisposed()
    {
        // clean-cancel パスでも _receiveLoopFault が公開され、Dispose 後の
        // 新規呼び出しが永久 await せずに ObjectDisposedException で即時失敗する。
        var (server, client) = ConnectPair();
        using var _ = server;
        var conn = new IpcConnection(client);
        conn.StartMultiplexedReceive();
        conn.Dispose();

        int id = conn.NextId();
        var req = IpcMessage.CreateSimple(id, RequestType);
        var task = conn.SendAndReceiveAsync(req).AsTask();
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.That(completed, Is.SameAs(task), "post-Dispose call must fail fast, not hang");
        Assert.CatchAsync<ObjectDisposedException>(async () => await task);
        Assert.That(PendingCount(conn), Is.EqualTo(0));
    }

    [Test]
    public void StartMultiplexedReceive_CalledTwice_Throws()
    {
        var (server, client) = ConnectPair();
        using var _ = server;
        using var conn = new IpcConnection(client);
        conn.StartMultiplexedReceive();

        Assert.Throws<InvalidOperationException>(() => conn.StartMultiplexedReceive());
    }

    [Test]
    public async Task ErrorResponse_ThrowsWorkerException_AndDictIsClean()
    {
        var (server, client) = ConnectPair();
        using var conn = new IpcConnection(client);
        conn.StartMultiplexedReceive();

        var workerCts = new CancellationTokenSource();
        var workerTask = Task.Run(async () =>
        {
            var req = await MessageSerializer.ReadMessageAsync(server, workerCts.Token);
            if (req == null) return;
            var resp = IpcMessage.CreateError(req.Id, "boom", "stack-here");
            await MessageSerializer.WriteMessageAsync(server, resp, workerCts.Token);
        });

        try
        {
            int id = conn.NextId();
            var req = IpcMessage.CreateSimple(id, RequestType);
            var ex = Assert.ThrowsAsync<FFmpegWorkerException>(
                async () => await conn.SendAndReceiveAsync(req));
            Assert.That(ex!.Message, Is.EqualTo("boom"));

            await WaitUntil(() => PendingCount(conn) == 0, TimeSpan.FromSeconds(5), "pending dict drains after error response");
            Assert.That(PendingCount(conn), Is.EqualTo(0));
        }
        finally
        {
            workerCts.Cancel();
            server.Dispose();
            try { await workerTask; }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
    }

    [Test]
    public async Task ProtocolCorruption_PendingRequestsObserveIOExceptionWithCause()
    {
        // 不正な length prefix で MessageSerializer が InvalidOperationException を
        // 投げて受信ループが死ぬケース。pending TCS は IOException (InnerException が
        // 元の InvalidOperationException) として観測されること。
        var (server, client) = ConnectPair();
        using var _ = server;
        using var conn = new IpcConnection(client);
        conn.StartMultiplexedReceive();

        int id = conn.NextId();
        var req = IpcMessage.CreateSimple(id, RequestType);
        var requestTask = conn.SendAndReceiveAsync(req).AsTask();

        // Drain the outbound request on the server side so the client's send completes before we inject
        // the fault. A real peer always reads the request; without draining, SendAsync's pipe write stays
        // pending (on Windows an unread pipe write does not return), so the request never reaches the
        // await-on-response stage and the fault injection cannot complete a send that is still stuck.
        var sentRequest = await AwaitWithResult(
            MessageSerializer.ReadMessageAsync(server).AsTask(), TimeSpan.FromSeconds(5));
        Assert.That(sentRequest!.Id, Is.EqualTo(id));
        await WaitUntil(() => PendingCount(conn) == 1, TimeSpan.FromSeconds(5), "request awaits its response");

        // 長さ -1 は MessageSerializer.ReadMessageAsync で
        // "Invalid message length" の InvalidOperationException を引き起こす。
        byte[] badLength = [0xFF, 0xFF, 0xFF, 0xFF];
        await server.WriteAsync(badLength);
        await server.FlushAsync();

        var completed = await Task.WhenAny(requestTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.That(completed, Is.SameAs(requestTask), "request must fault after the loop dies, not hang");
        var ex = Assert.CatchAsync<IOException>(async () => await requestTask);
        Assert.That(ex!.Message, Does.Contain("unexpected protocol or deserialization error"));
        Assert.That(ex.InnerException, Is.InstanceOf<InvalidOperationException>());

        await WaitUntil(() => PendingCount(conn) == 0, TimeSpan.FromSeconds(5), "pending dict drains after protocol error");
        Assert.That(PendingCount(conn), Is.EqualTo(0));
    }

    [Test]
    public async Task AfterReceiveLoopDies_NewRequestsFailFast()
    {
        // 受信ループ死亡後の新規リクエストはキャッシュされた _receiveLoopFault で
        // 即時失敗する。fault が公開されていないと TCS が誰にも完了されず永久 await になる。
        var (server, client) = ConnectPair();
        using var _ = server;
        using var conn = new IpcConnection(client);
        conn.StartMultiplexedReceive();

        int firstId = conn.NextId();
        var firstReq = IpcMessage.CreateSimple(firstId, RequestType);
        var firstTask = conn.SendAndReceiveAsync(firstReq).AsTask();

        // Drain the outbound request so the send completes and the request parks on its response TCS before
        // we kill the receive loop. (See ProtocolCorruption_PendingRequestsObserveIOExceptionWithCause for why.)
        var sentRequest = await AwaitWithResult(
            MessageSerializer.ReadMessageAsync(server).AsTask(), TimeSpan.FromSeconds(5));
        Assert.That(sentRequest!.Id, Is.EqualTo(firstId));
        await WaitUntil(() => PendingCount(conn) == 1, TimeSpan.FromSeconds(5), "first request awaits its response");

        byte[] badLength = [0xFF, 0xFF, 0xFF, 0xFF];
        await server.WriteAsync(badLength);
        await server.FlushAsync();

        var firstCompleted = await Task.WhenAny(firstTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.That(firstCompleted, Is.SameAs(firstTask), "first request must fault after the loop dies, not hang");
        Assert.CatchAsync<IOException>(async () => await firstTask);

        // ここでループは死んで _receiveLoopFault が公開されている。次のリクエストは
        // 待たずに即時 IOException を投げるはず (cached fault による fail-fast)。
        int secondId = conn.NextId();
        var secondReq = IpcMessage.CreateSimple(secondId, RequestType);
        var secondTask = conn.SendAndReceiveAsync(secondReq).AsTask();
        var completed = await Task.WhenAny(secondTask, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.That(completed, Is.SameAs(secondTask), "second request must fail fast, not hang");
        Assert.CatchAsync<IOException>(async () => await secondTask);
        Assert.That(PendingCount(conn), Is.EqualTo(0));
    }

    [Test]
    public async Task DisposeWhileRequestPending_ObservesCancellationNotIOException()
    {
        // 自己 Dispose 中は受信ループ完了を待ってからパイプを破棄する。
        // 逆順だと pending TCS に IOException("broken pipe") が伝播し、
        // 自プロセス由来と peer 由来の区別がつかなくなる。
        var (server, client) = ConnectPair();
        using var _ = server;
        var conn = new IpcConnection(client);
        try
        {
            conn.StartMultiplexedReceive();

            int id = conn.NextId();
            var req = IpcMessage.CreateSimple(id, RequestType);
            var requestTask = conn.SendAndReceiveAsync(req).AsTask();

            // Drain the outbound request so the send completes and the request parks on its response TCS
            // before Dispose. Without the drain the send stays stuck (there is no ct to cancel it), and
            // Dispose's pipe teardown raises ObjectDisposedException instead of the OperationCanceledException
            // this test expects from a self-dispose.
            var drained = await AwaitWithResult(
                MessageSerializer.ReadMessageAsync(server).AsTask(), TimeSpan.FromSeconds(5));
            Assert.That(drained!.Id, Is.EqualTo(id));
            await WaitUntil(() => PendingCount(conn) == 1, TimeSpan.FromSeconds(5), "request awaits its response");

            conn.Dispose();

            var ex = Assert.CatchAsync(async () => await requestTask);
            Assert.That(ex, Is.InstanceOf<OperationCanceledException>(),
                $"expected OperationCanceledException (self-dispose), got {ex?.GetType().Name}: {ex?.Message}");
        }
        finally
        {
            conn.Dispose();
        }
    }

    [Test]
    public void Dispose_CalledTwice_SecondCallIsNoOp()
    {
        // Interlocked.Exchange ガードが効いていれば 2 回目以降の Dispose は本体を
        // 通らない。ガードが緩むと SemaphoreSlim が再 Dispose されて
        // ObjectDisposedException が漏れ出すため、no-throw でガードをピン留めできる。
        var (server, client) = ConnectPair();
        using var _ = server;
        var conn = new IpcConnection(client);
        conn.StartMultiplexedReceive();

        Assert.DoesNotThrow(() => conn.Dispose());
        Assert.DoesNotThrow(() => conn.Dispose(), "second Dispose must be a no-op");
        Assert.DoesNotThrow(() => conn.Dispose(), "third Dispose must be a no-op");
    }

    [Test, Category("Slow")]
    public async Task Dispose_ProceedsWhenReceiveLoopDoesNotExitWithinTimeout()
    {
        // 受信ループが loopCt を無視してハングした場合、Dispose は 5 秒で
        // 待機を諦め、残りのリソース解放 (pipe / セマフォ / CTS) は完走させる契約。
        // タイムアウト境界が 5s から例えば 50ms に縮められたらこのテストが先に落ちる。
        // NOTE: 5 秒の実時間待機が発生する。短縮するなら受信ループ Wait 上限を可変にする
        //       internal API が必要。
        var pipe = new HangingPipeStream();
        var conn = new IpcConnection(pipe);
        conn.StartMultiplexedReceive();

        // 受信ループが確実に ReadAsync の hang に入った状態にしてから Dispose を呼ぶ。
        // pending dict に 1 件入った = SendAsync が完走 = ループは ReadAsync 待機中。
        int id = conn.NextId();
        var req = IpcMessage.CreateSimple(id, RequestType);
        _ = Task.Run(() => conn.SendAndReceiveAsync(req).AsTask());
        await WaitUntil(() => PendingCount(conn) == 1, TimeSpan.FromSeconds(2), "request enters pending dict");

        var sw = Stopwatch.StartNew();
        Assert.DoesNotThrow(() => conn.Dispose());
        sw.Stop();

        Assert.That(sw.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(4)),
            "Dispose must wait for the receive loop until the timeout fires");
        Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(8)),
            "Dispose must give up after the timeout instead of hanging indefinitely");
    }

    [Test]
    public async Task ForeignTokenCancellation_ObservedAsIOExceptionNotCleanCancel()
    {
        // 受信ループの clean-cancel フィルタは loopCt 自身のトークンを持つ OCE だけを
        // 黙って吸収する契約。別トークンの OCE は protocol-corruption と同じ経路で
        // IOException (InnerException = OCE) として pending リクエストに伝播する。
        // フィルタが loopCt 同一性チェックを失うと、外部由来の OCE が clean-cancel として
        // 握り潰されてしまい、呼び出し元はループ死亡を観測できなくなる。
        var foreignCts = new CancellationTokenSource();
        foreignCts.Cancel();
        var pipe = new FailingPipeStream(() => new OperationCanceledException(foreignCts.Token));
        using var conn = new IpcConnection(pipe);
        conn.StartMultiplexedReceive();

        int id = conn.NextId();
        var req = IpcMessage.CreateSimple(id, RequestType);
        var ex = Assert.CatchAsync<IOException>(async () => await conn.SendAndReceiveAsync(req));
        // メッセージ文字列ではなく InnerException 型でルートを判定する。
        // 「OCE を clean-cancel として握り潰すかどうか」だけが本テストの本質。
        Assert.That(ex!.InnerException, Is.InstanceOf<OperationCanceledException>());

        await WaitUntil(() => PendingCount(conn) == 0, TimeSpan.FromSeconds(5), "pending dict drains after foreign-token OCE");
    }

    [Test]
    public async Task Dispose_RethrowsFatalExceptionFromReceiveLoop()
    {
        // 受信ループが OOM/SOE/AVE を投げた場合、IPC のフレーム化エラーとして
        // 握りつぶしてはならない。Dispose は AggregateException から取り出した
        // 致命例外を ExceptionDispatchInfo で原型のまま再 throw する。
        using var pipe = new FailingPipeStream(() => new OutOfMemoryException("synthetic fatal"));
        var conn = new IpcConnection(pipe);
        try
        {
            conn.StartMultiplexedReceive();

            // ループが OOM を投げて Faulted で終わるまで観測してから Dispose を呼ぶ。
            // StartMultiplexedReceive 直後に Dispose だと、まだ loop body が走らないうちに
            // _receiveLoopCts.Cancel() が先に走り、OOM ではなく cancel 経路で終わる可能性がある。
            int id = conn.NextId();
            var req = IpcMessage.CreateSimple(id, RequestType);
            Assert.CatchAsync(async () => await conn.SendAndReceiveAsync(req));

            var oom = Assert.Throws<OutOfMemoryException>(() => conn.Dispose());
            Assert.That(oom!.Message, Is.EqualTo("synthetic fatal"));
        }
        finally
        {
            // OOM で Dispose が抜けた後の保険。Interlocked ガードのおかげで再呼び出しは no-op。
            conn.Dispose();
        }
    }

    [Test]
    public async Task BrokenPipe_PendingRequestsObserveIOException()
    {
        var (server, client) = ConnectPair();
        using var conn = new IpcConnection(client);
        conn.StartMultiplexedReceive();

        // worker は何も返さない。リクエストを 3 本立てて in-flight にしてから
        // server 側を強制クローズし、受信ループを IOException で終わらせる。
        var tasks = new List<Task<IpcMessage>>();
        for (int i = 0; i < 3; i++)
        {
            int id = conn.NextId();
            var req = IpcMessage.CreateSimple(id, RequestType);
            tasks.Add(conn.SendAndReceiveAsync(req).AsTask());
        }

        await WaitUntil(() => PendingCount(conn) == 3, TimeSpan.FromSeconds(5), "all 3 requests enter pending dict");

        server.Dispose();

        var exceptions = await Task.WhenAll(tasks.Select(async t =>
        {
            try { await t; return (Exception?)null; }
            catch (Exception ex) { return ex; }
        }));

        Assert.That(exceptions, Has.All.Not.Null);
        // 全タスクが broken-pipe 例外として観測される (OperationCanceledException ではない)。
        Assert.That(exceptions, Has.All.InstanceOf<IOException>());
        await WaitUntil(() => PendingCount(conn) == 0, TimeSpan.FromSeconds(5), "pending dict drains after broken pipe");
        Assert.That(PendingCount(conn), Is.EqualTo(0));
    }

    private static async Task WaitUntil(Func<bool> predicate, TimeSpan timeout, string description)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
        if (!predicate())
        {
            Assert.Fail($"Timeout {timeout.TotalMilliseconds}ms waiting for: {description}");
        }
    }

    /// <summary>
    /// Task の完了を上限時間で待つ。タイムアウト時は Assert.Fail でテストを落とす。
    /// 既に Task が faulted であれば内側の例外が再 throw される (呼び出し元で catch する想定)。
    /// </summary>
    private static async Task AwaitWithTimeout(Task task, TimeSpan timeout, string description)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task)
        {
            Assert.Fail($"Timeout {timeout.TotalMilliseconds}ms waiting for: {description}");
        }
        await task;
    }

    /// <summary>
    /// 結果付き Task の完了を上限時間で待つ。タイムアウト時は Assert.Fail でテストを落とす。
    /// </summary>
    private static async Task<T> AwaitWithResult<T>(Task<T> task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task)
        {
            Assert.Fail($"Timeout {timeout.TotalMilliseconds}ms waiting for task result.");
        }
        return await task;
    }

    /// <summary>
    /// ReadAsync が永久に完了しない PipeStream。Dispose の Wait タイムアウト経路を
    /// 検証する目的でだけ使う。
    /// </summary>
    private sealed class HangingPipeStream : PipeStream
    {
        public HangingPipeStream() : base(PipeDirection.InOut, 4096) { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            // 同期 Read は使わない想定だが、最低限ハングさせる。
            new TaskCompletionSource<int>().Task.GetAwaiter().GetResult();
            return 0;
        }

        public override int Read(Span<byte> buffer) => Read(Array.Empty<byte>(), 0, 0);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => new ValueTask<int>(new TaskCompletionSource<int>().Task);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => new TaskCompletionSource<int>().Task;

        public override void Write(byte[] buffer, int offset, int count) { }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    /// <summary>
    /// ReadAsync/Read で任意の例外を投げる PipeStream。Dispose 内 receive-loop の
    /// fail-fast 経路や fatal 再 throw 経路をテストするために使う。
    /// </summary>
    private sealed class FailingPipeStream : PipeStream
    {
        private readonly Func<Exception> _factory;

        public FailingPipeStream(Func<Exception> factory)
            : base(PipeDirection.InOut, 4096)
        {
            _factory = factory;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw _factory();

        public override int Read(Span<byte> buffer) => throw _factory();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => throw _factory();

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => throw _factory();

        public override void Write(byte[] buffer, int offset, int count) { }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }
}
