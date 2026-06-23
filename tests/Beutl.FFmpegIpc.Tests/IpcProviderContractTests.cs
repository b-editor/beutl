using System.IO.Pipes;
using System.Runtime.InteropServices;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.Providers;
using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegIpc.Transport;
using Beutl.Media;

namespace Beutl.FFmpegIpc.Tests;

/// <summary>
/// Pins the IPC client-side providers (relocated from the GPL worker into MIT Beutl.FFmpegIpc)
/// against a fake host driven over a real NamedPipe. The providers use the non-multiplexed
/// <see cref="IpcConnection.SendAndReceiveAsync"/> path, so the host answers requests sequentially.
/// </summary>
[TestFixture, NonParallelizable]
public class IpcProviderContractTests
{
    // 1x1 RgbaF16 frame: 4 channels * 2 bytes. The host writes a frame signature into the first
    // bytes of the shared buffer so the test can read back which frame the provider actually returned.
    private const int FrameDataLength = 8;
    private const long BufferCapacity = 4096;

    private static string NewName() => "beutl-ipc-prov-" + Guid.NewGuid().ToString("N")[..8];

    private static (NamedPipeServerStream Server, NamedPipeClientStream Client) ConnectPair()
    {
        string name = NewName();
        var server = new NamedPipeServerStream(
            name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var client = new NamedPipeClientStream(
            ".", name, PipeDirection.InOut, PipeOptions.Asynchronous);

        var serverConnectTask = server.WaitForConnectionAsync();
        client.Connect(TimeSpan.FromSeconds(5));
        serverConnectTask.GetAwaiter().GetResult();
        return (server, client);
    }

    private static SharedMemoryBuffer[] CreateBuffers() =>
        [SharedMemoryBuffer.Create(NewName(), BufferCapacity), SharedMemoryBuffer.Create(NewName(), BufferCapacity)];

    private static void DisposeBuffers(SharedMemoryBuffer[] buffers)
    {
        foreach (var b in buffers)
            b.Dispose();
    }

    private static long ReadFrameSignature(Bitmap bmp)
    {
        byte[] buf = new byte[FrameDataLength];
        Marshal.Copy(bmp.Data, buf, 0, FrameDataLength);
        return BitConverter.ToInt64(buf);
    }

    // Fake host: serves each RequestFrame by writing that frame's signature into the requested
    // buffer slot and replying ProvideFrame. Records the received frame indices in order.
    private static Task RunFrameServingHost(
        NamedPipeServerStream server, SharedMemoryBuffer[] buffers, List<long> received, object gate, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                var req = await MessageSerializer.ReadMessageAsync(server, ct);
                if (req == null)
                    return;

                var payload = req.GetPayload<RequestFrameMessage>()!;
                lock (gate)
                    received.Add(payload.FrameIndex);

                buffers[payload.BufferIndex].Write(BitConverter.GetBytes(payload.FrameIndex));

                var resp = IpcMessage.Create(req.Id, MessageType.ProvideFrame, new ProvideFrameMessage
                {
                    Width = 1,
                    Height = 1,
                    BytesPerPixel = FrameDataLength,
                    DataLength = FrameDataLength,
                    Premul = false,
                });
                await MessageSerializer.WriteMessageAsync(server, resp, ct);
            }
        }, ct);
    }

    // Fake host that answers every request with CancelEncode (the worker-cancel signal).
    private static Task RunCancelingHost(NamedPipeServerStream server, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                var req = await MessageSerializer.ReadMessageAsync(server, ct);
                if (req == null)
                    return;

                var resp = IpcMessage.CreateSimple(req.Id, MessageType.CancelEncode);
                await MessageSerializer.WriteMessageAsync(server, resp, ct);
            }
        }, ct);
    }

    // Fake host that replies with a ProvideFrame whose DataLength does not match the 1x1 RgbaF16
    // bitmap allocation, so a test can drive the provider's destination-size guard.
    private static Task RunBadDataLengthHost(NamedPipeServerStream server, int dataLength, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                var req = await MessageSerializer.ReadMessageAsync(server, ct);
                if (req == null)
                    return;

                var resp = IpcMessage.Create(req.Id, MessageType.ProvideFrame, new ProvideFrameMessage
                {
                    Width = 1,
                    Height = 1,
                    BytesPerPixel = FrameDataLength,
                    DataLength = dataLength,
                    Premul = false,
                });
                await MessageSerializer.WriteMessageAsync(server, resp, ct);
            }
        }, ct);
    }

    private static async Task WaitUntil(Func<bool> predicate, TimeSpan timeout, string description)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        if (!predicate())
            Assert.Fail($"Timeout {timeout.TotalMilliseconds}ms waiting for: {description}");
    }

    private static async Task StopHost(CancellationTokenSource cts, NamedPipeServerStream server, Task hostTask)
    {
        cts.Cancel();
        server.Dispose();
        try { await hostTask; }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    [Test]
    public async Task RenderFrame_SeekAfterPrefetch_ReturnsRequestedFrameAndIssuesFreshRequest()
    {
        // RenderFrame(0) arms a prefetch of frame 1. A seek to frame 5 must drain that stale prefetch
        // and issue a fresh RequestFrame{5}, returning frame 5's data — not the prefetched frame 1.
        var (server, client) = ConnectPair();
        var buffers = CreateBuffers();
        var received = new List<long>();
        var gate = new object();
        var hostCts = new CancellationTokenSource();
        var hostTask = RunFrameServingHost(server, buffers, received, gate, hostCts.Token);

        using var conn = new IpcConnection(client);
        // frameCount: 6 makes frame 5 the last frame, so RenderFrame(5) issues no post-seek prefetch.
        // This keeps the test self-contained: no dangling prefetch task survives into teardown to
        // raise an unobserved exception when the pipe/buffers are disposed.
        var provider = new IpcFrameProvider(conn, buffers, frameCount: 6, frameRate: new Rational(30, 1));

        try
        {
            using Bitmap frame0 = await provider.RenderFrame(0);
            using Bitmap frame5 = await provider.RenderFrame(5);

            Assert.Multiple(() =>
            {
                Assert.That(ReadFrameSignature(frame0), Is.EqualTo(0L), "first render returns frame 0");
                Assert.That(ReadFrameSignature(frame5), Is.EqualTo(5L), "seek returns frame 5, not the prefetched frame 1");
            });

            // render(0), prefetch(1, drained), fresh seek(5). Frame 5 is the last frame, so no further
            // prefetch fires — the host receives exactly [0, 1, 5].
            await WaitUntil(() => { lock (gate) return received.Count >= 3; },
                TimeSpan.FromSeconds(5), "host receives render + prefetch + fresh seek request");

            long[] firstThree;
            lock (gate)
                firstThree = received.Take(3).ToArray();
            Assert.That(firstThree, Is.EqualTo(new[] { 0L, 1L, 5L }),
                "the drained prefetch (1) proves a fresh RequestFrame{5} was issued after the seek");
        }
        finally
        {
            await StopHost(hostCts, server, hostTask);
            DisposeBuffers(buffers);
        }
    }

    [TestCase(FrameDataLength * 2)]
    [TestCase(FrameDataLength / 2)]
    public async Task RenderFrame_WhenWorkerReportsMismatchedDataLength_Throws(int dataLength)
    {
        var (server, client) = ConnectPair();
        var buffers = CreateBuffers();
        var hostCts = new CancellationTokenSource();
        var hostTask = RunBadDataLengthHost(server, dataLength, hostCts.Token);

        using var conn = new IpcConnection(client);
        // frameCount 1 makes frame 0 the last frame, so no prefetch is armed and nothing dangles.
        var provider = new IpcFrameProvider(conn, buffers, frameCount: 1, frameRate: new Rational(30, 1));

        try
        {
            Assert.That(async () => await provider.RenderFrame(0),
                Throws.TypeOf<InvalidOperationException>(),
                "a DataLength that does not match the bitmap allocation must be rejected, not copied");
        }
        finally
        {
            await StopHost(hostCts, server, hostTask);
            DisposeBuffers(buffers);
        }
    }

    [Test]
    public async Task RenderFrame_WhenHostCancels_ThrowsOperationCanceled()
    {
        var (server, client) = ConnectPair();
        var buffers = CreateBuffers();
        var hostCts = new CancellationTokenSource();
        var hostTask = RunCancelingHost(server, hostCts.Token);

        using var conn = new IpcConnection(client);
        var provider = new IpcFrameProvider(conn, buffers, frameCount: 100, frameRate: new Rational(30, 1));

        try
        {
            Assert.That(async () => await provider.RenderFrame(0),
                Throws.TypeOf<OperationCanceledException>());
        }
        finally
        {
            await StopHost(hostCts, server, hostTask);
            DisposeBuffers(buffers);
        }
    }

    [Test]
    public async Task Sample_WhenHostCancels_ThrowsOperationCanceled()
    {
        var (server, client) = ConnectPair();
        var buffers = CreateBuffers();
        var hostCts = new CancellationTokenSource();
        var hostTask = RunCancelingHost(server, hostCts.Token);

        using var conn = new IpcConnection(client);
        var provider = new IpcSampleProvider(conn, buffers, sampleCount: 48000, sampleRate: 48000);

        try
        {
            Assert.That(async () => await provider.Sample(0, 1024),
                Throws.TypeOf<OperationCanceledException>());
        }
        finally
        {
            await StopHost(hostCts, server, hostTask);
            DisposeBuffers(buffers);
        }
    }
}
