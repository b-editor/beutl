using System.Collections.Concurrent;
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
        // ct.ThrowIfCancellationRequested が _pendingRequests への登録より先に走ること
        // を確認する。登録が先だと finally 経由で消えるため、登録が起きないことの
        // 直接の証跡として、PendingCount が増えないかどうかは別経路の補強でしかない点に注意。
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
        // IpcConnection.cs の DroppedResponseHandler XML doc が
        // 「万一スローしても受信ループは継続」と契約している。
        // ハンドラ catch を狭めた変更が壊れたら、次のリクエストが永久ハングする。
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

            await WaitUntil(() => PendingCount(conn) == 1, TimeSpan.FromSeconds(5), "first request enters pending dict");
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

        await WaitUntil(() => PendingCount(conn) == 1, TimeSpan.FromSeconds(5), "second request enters pending dict");

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

                // PR で修正したキャンセル経路がリグレッションすると await が永久に
                // 返らなくなるため、フェイルファストで上限を切る。
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
        // _pendingRequests に同じ Id を二重登録しようとすると、TryAdd 化により
        // 2 つ目が InvalidOperationException で失敗し、1 つ目の TCS には影響しない。
        // 旧 indexer 代入では 1 つ目を黙って捨てて 1 つ目の awaiter が永久 await になっていた。
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
        // clean-cancel パスでも _receiveLoopFault が公開されることのテスト。
        // 旧コードでは terminationError 無しのとき fault を書かず、Dispose 後の
        // 新規呼び出しが永久 await した。
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
        // commit 9325ed5 で追加した catch (Exception) アームの回帰テスト。
        // 不正な length prefix を流し込み、MessageSerializer が
        // InvalidOperationException を投げて受信ループが死ぬケースを再現する。
        var (server, client) = ConnectPair();
        using var _ = server;
        using var conn = new IpcConnection(client);
        conn.StartMultiplexedReceive();

        int id = conn.NextId();
        var req = IpcMessage.CreateSimple(id, RequestType);
        var requestTask = conn.SendAndReceiveAsync(req).AsTask();

        await WaitUntil(() => PendingCount(conn) == 1, TimeSpan.FromSeconds(5), "request enters pending dict");

        // 長さ -1 は MessageSerializer.ReadMessageAsync で
        // "Invalid message length" の InvalidOperationException を引き起こす。
        byte[] badLength = [0xFF, 0xFF, 0xFF, 0xFF];
        await server.WriteAsync(badLength);
        await server.FlushAsync();

        var ex = Assert.CatchAsync<IOException>(async () => await requestTask);
        Assert.That(ex!.Message, Does.Contain("unexpected protocol or deserialization error"));
        Assert.That(ex.InnerException, Is.InstanceOf<InvalidOperationException>());

        await WaitUntil(() => PendingCount(conn) == 0, TimeSpan.FromSeconds(5), "pending dict drains after protocol error");
        Assert.That(PendingCount(conn), Is.EqualTo(0));
    }

    [Test]
    public async Task AfterReceiveLoopDies_NewRequestsFailFast()
    {
        // _receiveLoopFault のキャッシュが効いていないと、ループ死亡後に登録された
        // TCS は誰にも完了されず永久 await になる。
        // 1) 壊れたフレームでループを殺し、2) 最初のリクエストが IOException で
        // 返ったのを確認、3) 次のリクエストがキャッシュ済み fault で即時失敗する
        // ことを検証する。
        var (server, client) = ConnectPair();
        using var _ = server;
        using var conn = new IpcConnection(client);
        conn.StartMultiplexedReceive();

        int firstId = conn.NextId();
        var firstReq = IpcMessage.CreateSimple(firstId, RequestType);
        var firstTask = conn.SendAndReceiveAsync(firstReq).AsTask();
        await WaitUntil(() => PendingCount(conn) == 1, TimeSpan.FromSeconds(5), "first request enters pending dict");

        byte[] badLength = [0xFF, 0xFF, 0xFF, 0xFF];
        await server.WriteAsync(badLength);
        await server.FlushAsync();

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
        // commit 1d3304e5c で修正した Dispose 順序の回帰テスト。
        // 自己 Dispose 中は受信ループの完了を待ってからパイプを破棄する契約。
        // パイプ破棄を先にすると pending TCS に IOException("broken pipe") が
        // 伝播してしまい、自プロセス由来か peer 由来かが区別不能になる。
        var (server, client) = ConnectPair();
        using var _ = server;
        var conn = new IpcConnection(client);
        try
        {
            conn.StartMultiplexedReceive();

            int id = conn.NextId();
            var req = IpcMessage.CreateSimple(id, RequestType);
            var requestTask = conn.SendAndReceiveAsync(req).AsTask();
            await WaitUntil(() => PendingCount(conn) == 1, TimeSpan.FromSeconds(5), "request enters pending dict");

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
}
