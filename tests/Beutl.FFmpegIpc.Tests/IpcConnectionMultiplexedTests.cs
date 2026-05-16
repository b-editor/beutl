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

        // Worker 役: 受信した順にレスポンスを返す。
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

            await Task.WhenAll(tasks.Select(t => t.Task));
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
    public async Task AlreadyCanceledToken_ThrowsBeforeRegistering()
    {
        var (server, client) = ConnectPair();
        using var _ = server;
        using var conn = new IpcConnection(client);
        conn.StartMultiplexedReceive();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        int id = conn.NextId();
        var req = IpcMessage.CreateSimple(id, RequestType);
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

            // 送信完了 (= pending に積まれた) を観測してからキャンセル。Task.Delay よりも安定。
            await WaitUntil(() => PendingCount(conn) == 1, TimeSpan.FromSeconds(2), "request enters pending dict");
            cts.Cancel();

            var oce = Assert.CatchAsync<OperationCanceledException>(async () => await requestTask);
            Assert.That(oce!.CancellationToken, Is.EqualTo(cts.Token));

            await WaitUntil(() => PendingCount(conn) == 0, TimeSpan.FromSeconds(2), "pending dict drains after cancel");
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

            await WaitUntil(() => PendingCount(conn) == 1, TimeSpan.FromSeconds(2), "request enters pending dict");
            cts.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await requestTask);
            await WaitUntil(() => PendingCount(conn) == 0, TimeSpan.FromSeconds(2), "pending dict drains after cancel");

            // 遅延レスポンスを送り込む → dropped ハンドラに来るはず。
            var resp = IpcMessage.CreateSimple(id, ResponseType);
            await MessageSerializer.WriteMessageAsync(server, resp);

            await WaitUntil(() => dropped.Count >= 1, TimeSpan.FromSeconds(2), "DroppedResponseHandler is invoked for late response");
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

            await WaitUntil(() => PendingCount(conn) == 0, TimeSpan.FromSeconds(2), "pending dict drains after error response");
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

        await WaitUntil(() => PendingCount(conn) == 3, TimeSpan.FromSeconds(2), "all 3 requests enter pending dict");

        server.Dispose();

        var exceptions = await Task.WhenAll(tasks.Select(async t =>
        {
            try { await t; return (Exception?)null; }
            catch (Exception ex) { return ex; }
        }));

        Assert.That(exceptions, Has.All.Not.Null);
        // 全タスクが broken-pipe 例外として観測される (OperationCanceledException ではない)。
        Assert.That(exceptions, Has.All.InstanceOf<IOException>());
        await WaitUntil(() => PendingCount(conn) == 0, TimeSpan.FromSeconds(2), "pending dict drains after broken pipe");
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
}
