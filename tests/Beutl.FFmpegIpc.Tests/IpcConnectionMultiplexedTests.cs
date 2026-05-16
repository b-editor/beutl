using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Reflection;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Transport;

namespace Beutl.FFmpegIpc.Tests;

/// <summary>
/// IpcConnection.SendAndReceiveMultiplexedAsync のキャンセル順序と
/// 競合パスを検証する。NamedPipe を使ったクロスプラットフォーム動作。
/// </summary>
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

    private static int PendingCount(IpcConnection conn)
    {
        var field = typeof(IpcConnection).GetField(
            "_pendingRequests", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var dict = (System.Collections.IDictionary)field.GetValue(conn)!;
        return dict.Count;
    }

    [Test]
    public async Task ConcurrentRequests_AllReceiveCorrectResponses()
    {
        var (server, client) = ConnectPair();
        using var conn = new IpcConnection(client);
        conn.StartMultiplexedReceive();

        // 「Worker」役: 受信したリクエストをそのまま Pong として返す。
        var workerCts = new CancellationTokenSource();
        var workerTask = Task.Run(async () =>
        {
            try
            {
                while (!workerCts.IsCancellationRequested)
                {
                    var req = await MessageSerializer.ReadMessageAsync(server, workerCts.Token);
                    if (req == null) return;
                    var resp = IpcMessage.CreateSimple(req.Id, ResponseType);
                    await MessageSerializer.WriteMessageAsync(server, resp, workerCts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        });

        try
        {
            var tasks = new List<Task<IpcMessage>>();
            for (int i = 0; i < 5; i++)
            {
                int id = conn.NextId();
                var req = IpcMessage.CreateSimple(id, RequestType);
                tasks.Add(conn.SendAndReceiveAsync(req).AsTask());
            }

            var responses = await Task.WhenAll(tasks);
            Assert.That(responses.Length, Is.EqualTo(5));
            Assert.That(responses.Select(r => r.Type), Is.All.EqualTo(ResponseType));

            // Pending dictionary は空に戻っているはず。
            Assert.That(PendingCount(conn), Is.EqualTo(0));
        }
        finally
        {
            workerCts.Cancel();
            server.Dispose();
            try { await workerTask; } catch { }
        }
    }

    [Test]
    public async Task AlreadyCanceledToken_ThrowsBeforeRegistering()
    {
        var (server, client) = ConnectPair();
        using var conn = new IpcConnection(client);
        conn.StartMultiplexedReceive();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            int id = conn.NextId();
            var req = IpcMessage.CreateSimple(id, RequestType);
            Assert.CatchAsync<OperationCanceledException>(
                async () => await conn.SendAndReceiveAsync(req, cts.Token));

            // dict には何も登録されていない。
            Assert.That(PendingCount(conn), Is.EqualTo(0));
        }
        finally
        {
            server.Dispose();
            await Task.Yield();
        }
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

            // 送信完了を待ってからキャンセル。
            await Task.Delay(50);
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () => await requestTask);

            // dict は撤去済み。
            await WaitUntil(() => PendingCount(conn) == 0, TimeSpan.FromSeconds(2));
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

            await Task.Delay(50);
            cts.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await requestTask);
            await WaitUntil(() => PendingCount(conn) == 0, TimeSpan.FromSeconds(2));

            // 遅延レスポンスを送り込む → dropped ハンドラに来るはず。
            var resp = IpcMessage.CreateSimple(id, ResponseType);
            await MessageSerializer.WriteMessageAsync(server, resp);

            await WaitUntil(() => dropped.Count >= 1, TimeSpan.FromSeconds(2));
            Assert.That(dropped.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(dropped.Any(m => m.Id == id), Is.True);
        }
        finally
        {
            server.Dispose();
        }
    }

    [Test]
    public async Task RaceCancelAndResponse_NoLeak_StressLoop()
    {
        for (int iter = 0; iter < 50; iter++)
        {
            var (server, client) = ConnectPair();
            using var conn = new IpcConnection(client);

            var dropped = new ConcurrentQueue<IpcMessage>();
            conn.DroppedResponseHandler = msg => dropped.Enqueue(msg);
            conn.StartMultiplexedReceive();

            var workerCts = new CancellationTokenSource();
            var workerTask = Task.Run(async () =>
            {
                try
                {
                    var req = await MessageSerializer.ReadMessageAsync(server, workerCts.Token);
                    if (req == null) return;
                    // worker からは即時にレスポンス。
                    var resp = IpcMessage.CreateSimple(req.Id, ResponseType);
                    await MessageSerializer.WriteMessageAsync(server, resp, workerCts.Token);
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
            });

            using var cts = new CancellationTokenSource();
            try
            {
                int id = conn.NextId();
                var req = IpcMessage.CreateSimple(id, RequestType);
                var requestTask = conn.SendAndReceiveAsync(req, cts.Token).AsTask();

                // 受信ループとキャンセルがほぼ同時に走るよう仕向ける。
                cts.Cancel();

                try
                {
                    await requestTask;
                    // 正常完了でも問題なし。
                }
                catch (OperationCanceledException)
                {
                    // キャンセル勝ち。
                }
            }
            finally
            {
                workerCts.Cancel();
                server.Dispose();
                try { await workerTask; } catch { }
            }

            // 反復毎に dict は空に戻っているべき。
            await WaitUntil(() => PendingCount(conn) == 0, TimeSpan.FromSeconds(2));
            Assert.That(PendingCount(conn), Is.EqualTo(0), $"iter={iter}");
        }
    }

    private static async Task WaitUntil(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }
}
