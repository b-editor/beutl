using System.IO.Pipes;
using System.Runtime.InteropServices;
using Beutl.FFmpegIpc;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.Providers;
using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegIpc.Transport;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

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
    private const int Bgra8888BytesPerPixel = 4;
    // RgbaF16 stride: 4 channels * 2 bytes. Distinct from FrameDataLength, a whole 1x1 frame.
    private const int RgbaF16BytesPerPixel = 8;
    // Stereo32BitFloat: 2 channels * 4 bytes.
    private const int Stereo32BitFloatBytesPerSample = 8;
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
                    BytesPerPixel = RgbaF16BytesPerPixel,
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

    // Fake host that replies to every request with one fixed ProvideFrame, so a test can drive the
    // provider's frame-validation guards with specific Width/Height/DataLength values.
    private static Task RunMalformedFrameHost(
        NamedPipeServerStream server, int width, int height, int dataLength, CancellationToken ct,
        int bytesPerPixel = RgbaF16BytesPerPixel)
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
                    Width = width,
                    Height = height,
                    BytesPerPixel = bytesPerPixel,
                    DataLength = dataLength,
                    Premul = false,
                });
                await MessageSerializer.WriteMessageAsync(server, resp, ct);
            }
        }, ct);
    }

    // Fake host that serves frame 0 with a mismatched DataLength (failing the destination-size guard)
    // but answers every other frame normally. Records received frame indices so a test can assert that
    // a rejected frame did not leave a stray prefetch request behind.
    private static Task RunFrame0BadDataLengthHost(
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

                if (payload.FrameIndex == 0)
                {
                    // Oversized DataLength would overrun the 1x1 RgbaF16 destination if not rejected.
                    var bad = IpcMessage.Create(req.Id, MessageType.ProvideFrame, new ProvideFrameMessage
                    {
                        Width = 1,
                        Height = 1,
                        BytesPerPixel = RgbaF16BytesPerPixel,
                        DataLength = FrameDataLength * 2,
                        Premul = false,
                    });
                    await MessageSerializer.WriteMessageAsync(server, bad, ct);
                    continue;
                }

                buffers[payload.BufferIndex].Write(BitConverter.GetBytes(payload.FrameIndex));
                var resp = IpcMessage.Create(req.Id, MessageType.ProvideFrame, new ProvideFrameMessage
                {
                    Width = 1,
                    Height = 1,
                    BytesPerPixel = RgbaF16BytesPerPixel,
                    DataLength = FrameDataLength,
                    Premul = false,
                });
                await MessageSerializer.WriteMessageAsync(server, resp, ct);
            }
        }, ct);
    }

    // Fake host that fails the first request for frame 1 with an error response (so the provider's
    // prefetch task faults) and answers every other request — including the second frame 1 request —
    // normally. Lets a test prove a faulted prefetch is not pinned and a retry recovers.
    private static Task RunPrefetchFaultsOnceHost(
        NamedPipeServerStream server, SharedMemoryBuffer[] buffers, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            bool frame1Faulted = false;
            while (!ct.IsCancellationRequested)
            {
                var req = await MessageSerializer.ReadMessageAsync(server, ct);
                if (req == null)
                    return;

                var payload = req.GetPayload<RequestFrameMessage>()!;
                if (payload.FrameIndex == 1 && !frame1Faulted)
                {
                    frame1Faulted = true;
                    await MessageSerializer.WriteMessageAsync(
                        server, IpcMessage.CreateError(req.Id, "injected prefetch failure"), ct);
                    continue;
                }

                buffers[payload.BufferIndex].Write(BitConverter.GetBytes(payload.FrameIndex));
                var resp = IpcMessage.Create(req.Id, MessageType.ProvideFrame, new ProvideFrameMessage
                {
                    Width = 1,
                    Height = 1,
                    BytesPerPixel = RgbaF16BytesPerPixel,
                    DataLength = FrameDataLength,
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

    [Test]
    public async Task RenderFrame_WhenWorkerReportsBgra8888Frame_ReturnsBgra8888Bitmap()
    {
        var (server, client) = ConnectPair();
        var buffers = CreateBuffers();
        var hostCts = new CancellationTokenSource();
        var hostTask = RunMalformedFrameHost(
            server, width: 1, height: 1, dataLength: Bgra8888BytesPerPixel, ct: hostCts.Token,
            bytesPerPixel: Bgra8888BytesPerPixel);

        using var conn = new IpcConnection(client);
        var provider = new IpcFrameProvider(conn, buffers, frameCount: 1, frameRate: new Rational(30, 1));

        try
        {
            using Bitmap bitmap = await provider.RenderFrame(0);

            Assert.Multiple(() =>
            {
                Assert.That(bitmap.ColorType, Is.EqualTo(BitmapColorType.Bgra8888));
                Assert.That(bitmap.ColorSpace, Is.EqualTo(BitmapColorSpace.Srgb));
                Assert.That(bitmap.BytesPerPixel, Is.EqualTo(Bgra8888BytesPerPixel));
                Assert.That(bitmap.ByteCount, Is.EqualTo(Bgra8888BytesPerPixel));
            });
        }
        finally
        {
            await StopHost(hostCts, server, hostTask);
            DisposeBuffers(buffers);
        }
    }

    [TestCase(FrameDataLength * 2)]
    [TestCase(FrameDataLength / 2)]
    [TestCase(0)]
    public async Task RenderFrame_WhenWorkerReportsMismatchedDataLength_Throws(int dataLength)
    {
        var (server, client) = ConnectPair();
        var buffers = CreateBuffers();
        var hostCts = new CancellationTokenSource();
        var hostTask = RunMalformedFrameHost(server, width: 1, height: 1, dataLength: dataLength, ct: hostCts.Token);

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
    public async Task RenderFrame_WhenWorkerReportsUnsupportedBytesPerPixel_Throws()
    {
        var (server, client) = ConnectPair();
        var buffers = CreateBuffers();
        var hostCts = new CancellationTokenSource();
        var hostTask = RunMalformedFrameHost(
            server, width: 1, height: 1, dataLength: 3, ct: hostCts.Token,
            bytesPerPixel: 3);

        using var conn = new IpcConnection(client);
        var provider = new IpcFrameProvider(conn, buffers, frameCount: 1, frameRate: new Rational(30, 1));

        try
        {
            Assert.That(async () => await provider.RenderFrame(0),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("Unsupported frame BytesPerPixel"));
        }
        finally
        {
            await StopHost(hostCts, server, hostTask);
            DisposeBuffers(buffers);
        }
    }

    [TestCase(0, 1)]
    [TestCase(1, 0)]
    public async Task RenderFrame_WhenFrameDimensionsAreNonPositive_Throws(int width, int height)
    {
        // DataLength 0 matches a zero-area frame's expected size, so only an explicit dimension guard
        // rejects it — without one the provider would hand back an empty bitmap instead of throwing.
        var (server, client) = ConnectPair();
        var buffers = CreateBuffers();
        var hostCts = new CancellationTokenSource();
        var hostTask = RunMalformedFrameHost(server, width, height, dataLength: 0, ct: hostCts.Token);

        using var conn = new IpcConnection(client);
        var provider = new IpcFrameProvider(conn, buffers, frameCount: 1, frameRate: new Rational(30, 1));

        try
        {
            Assert.That(async () => await provider.RenderFrame(0),
                Throws.TypeOf<InvalidOperationException>(),
                "a frame with a non-positive dimension must be rejected, not turned into an empty bitmap");
        }
        finally
        {
            await StopHost(hostCts, server, hostTask);
            DisposeBuffers(buffers);
        }
    }

    [Test]
    public async Task RenderFrame_WhenFrameExceedsBufferCapacity_ThrowsFromReadAndDoesNotLeak()
    {
        // 32x32 RgbaF16 = 8192 bytes > BufferCapacity (4096). The DataLength guard passes (8192 == the
        // 32x32 RgbaF16 size), so the overflow only surfaces from SharedMemoryBuffer.Read's capacity check
        // AFTER the bitmap is allocated. The provider must let that throw propagate (its catch disposes the
        // freshly allocated native bitmap so the throw can't leak it).
        var (server, client) = ConnectPair();
        var buffers = CreateBuffers();
        var hostCts = new CancellationTokenSource();
        const int oversizedDataLength = 32 * 32 * RgbaF16BytesPerPixel; // 8192 > BufferCapacity 4096
        var hostTask = RunMalformedFrameHost(
            server, width: 32, height: 32, dataLength: oversizedDataLength, ct: hostCts.Token);

        using var conn = new IpcConnection(client);
        var provider = new IpcFrameProvider(conn, buffers, frameCount: 1, frameRate: new Rational(30, 1));

        try
        {
            Assert.That(async () => await provider.RenderFrame(0),
                Throws.TypeOf<ArgumentOutOfRangeException>(),
                "a frame larger than the shared buffer must surface the Read capacity overflow as a throw");

            // The native Bitmap dispose on the throw path can't be observed in-process, but a leak-free
            // failure must at least leave no committed state: the failed render must not count as a
            // completed frame (FramesRendered++ runs only after BuildBitmap succeeds).
            Assert.That(provider.FramesRendered, Is.EqualTo(0),
                "a render that throws from Read must not count as a completed frame");
        }
        finally
        {
            await StopHost(hostCts, server, hostTask);
            DisposeBuffers(buffers);
        }
    }

    [Test]
    public async Task RenderFrame_WhenValidationFails_DoesNotArmPrefetch()
    {
        // A frame that fails the DataLength guard must not arm a prefetch of the next frame. With frame 0
        // rejected, a later RenderFrame(2) leaves the host having seen only [0, 2] — no stray prefetch 1.
        var (server, client) = ConnectPair();
        var buffers = CreateBuffers();
        var received = new List<long>();
        var gate = new object();
        var hostCts = new CancellationTokenSource();
        var hostTask = RunFrame0BadDataLengthHost(server, buffers, received, gate, hostCts.Token);

        using var conn = new IpcConnection(client);
        // frameCount 3: frame 0 is not the last frame, so a misplaced guard *would* arm a prefetch of 1.
        // frame 2 is the last frame, so the successful render issues no further prefetch and nothing dangles.
        var provider = new IpcFrameProvider(conn, buffers, frameCount: 3, frameRate: new Rational(30, 1));

        try
        {
            Assert.That(async () => await provider.RenderFrame(0),
                Throws.TypeOf<InvalidOperationException>(),
                "frame 0's mismatched DataLength is rejected");

            using Bitmap frame2 = await provider.RenderFrame(2);
            Assert.That(ReadFrameSignature(frame2), Is.EqualTo(2L), "the provider stays usable after the rejected frame");

            long[] requests;
            lock (gate)
                requests = received.ToArray();
            Assert.That(requests, Is.EqualTo(new[] { 0L, 2L }),
                "a rejected frame must not have armed a prefetch (no stray request 1 between 0 and 2)");
        }
        finally
        {
            await StopHost(hostCts, server, hostTask);
            DisposeBuffers(buffers);
        }
    }

    [Test]
    public async Task RenderFrame_WhenPrefetchFaults_RecoversOnRetry()
    {
        // A faulted prefetch must not pin the provider. RenderFrame(0) arms a prefetch of frame 1; the
        // host faults it, so the matching RenderFrame(1) throws — but a retry must issue a fresh request
        // and recover instead of re-throwing the pinned faulted task forever.
        var (server, client) = ConnectPair();
        var buffers = CreateBuffers();
        var hostCts = new CancellationTokenSource();
        var hostTask = RunPrefetchFaultsOnceHost(server, buffers, hostCts.Token);

        using var conn = new IpcConnection(client);
        // frameCount 2: frame 0 arms the prefetch of 1; the recovered frame 1 is last, so nothing dangles.
        var provider = new IpcFrameProvider(conn, buffers, frameCount: 2, frameRate: new Rational(30, 1));

        try
        {
            using Bitmap frame0 = await provider.RenderFrame(0);
            Assert.That(ReadFrameSignature(frame0), Is.EqualTo(0L), "frame 0 renders before the prefetch faults");

            Assert.That(async () => await provider.RenderFrame(1),
                Throws.TypeOf<FFmpegWorkerException>(),
                "the faulted prefetch surfaces on the matching request");

            using Bitmap frame1 = await provider.RenderFrame(1);
            Assert.That(ReadFrameSignature(frame1), Is.EqualTo(1L),
                "the retry recovers because the faulted prefetch was not pinned to the provider");
        }
        finally
        {
            await StopHost(hostCts, server, hostTask);
            DisposeBuffers(buffers);
        }
    }

    // Captures every UnobservedTaskException raised on the finalizer thread while subscribed, so a test
    // can prove a provider's Dispose drained its in-flight prefetch instead of leaking it to the GC.
    private sealed class UnobservedTaskExceptionWatcher : IDisposable
    {
        private readonly List<Exception> _exceptions = [];
        private readonly object _gate = new();

        public UnobservedTaskExceptionWatcher()
        {
            TaskScheduler.UnobservedTaskException += OnUnobserved;
        }

        private void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            lock (_gate)
                _exceptions.Add(e.Exception);
            // Mark observed so the process-level escalation policy can't tear down the test host.
            e.SetObserved();
        }

        // Force two finalizer passes so any unobserved Task that the GC reclaimed has had its
        // UnobservedTaskException raised before we inspect the captured list.
        public Exception[] Drain()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            lock (_gate)
                return _exceptions.ToArray();
        }

        public void Dispose() => TaskScheduler.UnobservedTaskException -= OnUnobserved;
    }

    [Test]
    public async Task Dispose_ObservesInFlightFaultedFramePrefetch_NoUnobservedTaskException()
    {
        // RenderFrame(0) arms a prefetch of frame 1; the host faults that prefetch. We then Dispose the
        // provider WITHOUT consuming frame 1, so the faulted prefetch task is still in flight and is dropped
        // by Dispose. Its fault-swallowing continuation must observe the exception so the GC never raises an
        // UnobservedTaskException attributable to the provider.
        using var watcher = new UnobservedTaskExceptionWatcher();
        var (server, client) = ConnectPair();
        var buffers = CreateBuffers();
        var hostCts = new CancellationTokenSource();
        var hostTask = RunPrefetchFaultsOnceHost(server, buffers, hostCts.Token);

        using var conn = new IpcConnection(client);
        // frameCount 3: frame 0 arms a prefetch of frame 1, which the host faults. Frame 1 is never consumed.
        var provider = new IpcFrameProvider(conn, buffers, frameCount: 3, frameRate: new Rational(30, 1));

        try
        {
            using (Bitmap frame0 = await provider.RenderFrame(0))
                Assert.That(ReadFrameSignature(frame0), Is.EqualTo(0L), "frame 0 renders and arms the prefetch of frame 1");

            // Let the prefetch actually fault before we drop it, so we exercise the faulted-task path.
            await WaitUntil(() => provider.IsPrefetchFaultedForTest(),
                TimeSpan.FromSeconds(5), "the in-flight frame prefetch faults");

            // Drop the faulted prefetch without ever awaiting it via RenderFrame(1).
            provider.Dispose();
            provider.Dispose(); // idempotent: a second Dispose must be a no-op.

            Exception[] unobserved = watcher.Drain();
            Assert.That(unobserved, Is.Empty,
                "Dispose must observe the dropped faulted prefetch so no UnobservedTaskException is raised");
        }
        finally
        {
            await StopHost(hostCts, server, hostTask);
            DisposeBuffers(buffers);
        }
    }

    [Test]
    public async Task Dispose_ObservesInFlightFaultedSamplePrefetch_NoUnobservedTaskException()
    {
        // Consuming chunk 0 arms a prefetch of chunk 1 (offset == sampleRate); the host faults it. We then
        // Dispose the provider WITHOUT consuming chunk 1, so the faulted sample prefetch is dropped by
        // Dispose. Its observing continuation must mark the fault so no UnobservedTaskException is raised,
        // and Dispose must also free the cached chunk.
        const long sampleRate = 4;
        using var watcher = new UnobservedTaskExceptionWatcher();
        var (server, client) = ConnectPair();
        var buffers = CreateBuffers();
        var hostCts = new CancellationTokenSource();
        var hostTask = RunSamplePrefetchFaultsOnceHost(server, buffers, sampleRate, hostCts.Token);

        using var conn = new IpcConnection(client);
        // sampleCount 12 == 3 chunks of 4, so chunk 1's prefetch arms after chunk 0 and is not last.
        var provider = new IpcSampleProvider(conn, buffers, sampleCount: 12, sampleRate: sampleRate);

        try
        {
            using (Pcm<Stereo32BitFloat> chunk0 = await provider.Sample(0, sampleRate))
                Assert.That(ReadSampleSignature(chunk0), Is.EqualTo(0L), "chunk 0 loads and arms the prefetch of chunk 1");

            await WaitUntil(() => provider.IsPrefetchFaultedForTest(),
                TimeSpan.FromSeconds(5), "the in-flight sample prefetch faults");

            provider.Dispose();
            provider.Dispose(); // idempotent: a second Dispose must be a no-op.

            Exception[] unobserved = watcher.Drain();
            Assert.That(unobserved, Is.Empty,
                "Dispose must observe the dropped faulted sample prefetch so no UnobservedTaskException is raised");
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

    // ---- Sample-side host harness ----

    // The host writes a chunk signature (its offset) into the first 8 bytes of the requested audio
    // buffer so a test can read back which chunk it actually got.

    // Reads the first sample (8 bytes) the provider returned, interpreted as the chunk-offset signature.
    private static long ReadSampleSignature(Pcm<Stereo32BitFloat> pcm)
    {
        byte[] buf = new byte[Stereo32BitFloatBytesPerSample];
        Marshal.Copy(pcm.Data, buf, 0, Stereo32BitFloatBytesPerSample);
        return BitConverter.ToInt64(buf);
    }

    // Fake host: serves each RequestSample by writing that chunk's signature (its offset) into the
    // requested buffer slot and replying ProvideSample with a self-consistent NumSamples/DataLength.
    private static Task RunSampleServingHost(NamedPipeServerStream server, SharedMemoryBuffer[] buffers, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                var req = await MessageSerializer.ReadMessageAsync(server, ct);
                if (req == null)
                    return;

                var payload = req.GetPayload<RequestSampleMessage>()!;
                int numSamples = (int)payload.Length;
                // Signature occupies the first 8 bytes (one Stereo32BitFloat sample); requires numSamples >= 1.
                buffers[payload.BufferIndex].Write(BitConverter.GetBytes(payload.Offset));

                var resp = IpcMessage.Create(req.Id, MessageType.ProvideSample, new ProvideSampleMessage
                {
                    NumSamples = numSamples,
                    DataLength = numSamples * Stereo32BitFloatBytesPerSample,
                });
                await MessageSerializer.WriteMessageAsync(server, resp, ct);
            }
        }, ct);
    }

    // Fake host that replies to every request with one fixed ProvideSample, so a test can drive the
    // provider's NumSamples/DataLength validation with specific values.
    private static Task RunMalformedSampleHost(
        NamedPipeServerStream server, int numSamples, int dataLength, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                var req = await MessageSerializer.ReadMessageAsync(server, ct);
                if (req == null)
                    return;

                var resp = IpcMessage.Create(req.Id, MessageType.ProvideSample, new ProvideSampleMessage
                {
                    NumSamples = numSamples,
                    DataLength = dataLength,
                });
                await MessageSerializer.WriteMessageAsync(server, resp, ct);
            }
        }, ct);
    }

    // Fake host that answers every request with one fixed error response, so a test can prove an injected
    // worker error surfaces as FFmpegWorkerException from the sample path (not InvalidOperationException).
    private static Task RunErroringSampleHost(NamedPipeServerStream server, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                var req = await MessageSerializer.ReadMessageAsync(server, ct);
                if (req == null)
                    return;

                await MessageSerializer.WriteMessageAsync(
                    server, IpcMessage.CreateError(req.Id, "injected sample failure"), ct);
            }
        }, ct);
    }

    // Fake host that fails the first request for the second chunk (offset == sampleRate) with an error
    // response (so the provider's prefetch task faults) and answers every other request — including the
    // retry of that chunk — normally. Lets a test prove a faulted sample prefetch is not pinned.
    private static Task RunSamplePrefetchFaultsOnceHost(
        NamedPipeServerStream server, SharedMemoryBuffer[] buffers, long sampleRate, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            bool secondChunkFaulted = false;
            while (!ct.IsCancellationRequested)
            {
                var req = await MessageSerializer.ReadMessageAsync(server, ct);
                if (req == null)
                    return;

                var payload = req.GetPayload<RequestSampleMessage>()!;
                if (payload.Offset == sampleRate && !secondChunkFaulted)
                {
                    secondChunkFaulted = true;
                    await MessageSerializer.WriteMessageAsync(
                        server, IpcMessage.CreateError(req.Id, "injected sample prefetch failure"), ct);
                    continue;
                }

                int numSamples = (int)payload.Length;
                buffers[payload.BufferIndex].Write(BitConverter.GetBytes(payload.Offset));
                var resp = IpcMessage.Create(req.Id, MessageType.ProvideSample, new ProvideSampleMessage
                {
                    NumSamples = numSamples,
                    DataLength = numSamples * Stereo32BitFloatBytesPerSample,
                });
                await MessageSerializer.WriteMessageAsync(server, resp, ct);
            }
        }, ct);
    }

    [TestCase(8 * Stereo32BitFloatBytesPerSample)]   // oversized: would overrun the native Pcm
    [TestCase(2 * Stereo32BitFloatBytesPerSample)]   // undersized vs NumSamples
    public async Task Sample_WhenWorkerReportsMismatchedDataLength_Throws(int dataLength)
    {
        // The Pcm capacity is NumSamples * 8. A DataLength that disagrees with NumSamples must be rejected
        // before the Read, not used as the copy length (which would over/under-run the native allocation).
        var (server, client) = ConnectPair();
        var buffers = CreateBuffers();
        var hostCts = new CancellationTokenSource();
        var hostTask = RunMalformedSampleHost(server, numSamples: 4, dataLength, hostCts.Token);

        using var conn = new IpcConnection(client);
        var provider = new IpcSampleProvider(conn, buffers, sampleCount: 4, sampleRate: 4);

        try
        {
            Assert.That(async () => await provider.Sample(0, 4),
                Throws.TypeOf<InvalidOperationException>(),
                "a DataLength that does not match NumSamples * 8 must be rejected, not used as the copy length");
        }
        finally
        {
            await StopHost(hostCts, server, hostTask);
            DisposeBuffers(buffers);
        }
    }

    [Test]
    public async Task Sample_WhenHostErrors_ThrowsFFmpegWorkerException()
    {
        // An injected worker error must surface as FFmpegWorkerException (raised by SendAndReceiveAsync),
        // mirroring the frame-side guarantee — not be re-wrapped into InvalidOperationException.
        var (server, client) = ConnectPair();
        var buffers = CreateBuffers();
        var hostCts = new CancellationTokenSource();
        var hostTask = RunErroringSampleHost(server, hostCts.Token);

        using var conn = new IpcConnection(client);
        var provider = new IpcSampleProvider(conn, buffers, sampleCount: 4, sampleRate: 4);

        try
        {
            Assert.That(async () => await provider.Sample(0, 4),
                Throws.TypeOf<FFmpegWorkerException>(),
                "an injected worker error must surface as FFmpegWorkerException, not InvalidOperationException");
        }
        finally
        {
            await StopHost(hostCts, server, hostTask);
            DisposeBuffers(buffers);
        }
    }

    [Test]
    public async Task Sample_WhenPrefetchFaults_RecoversOnRetry()
    {
        // A faulted sample prefetch must not pin the provider. Consuming chunk 0 arms a prefetch of chunk 1
        // (offset == sampleRate); the host faults it, so the Sample that hits chunk 1 throws — but a retry
        // must issue a fresh request and recover instead of re-throwing the pinned faulted task forever.
        const long sampleRate = 4;
        var (server, client) = ConnectPair();
        var buffers = CreateBuffers();
        var hostCts = new CancellationTokenSource();
        var hostTask = RunSamplePrefetchFaultsOnceHost(server, buffers, sampleRate, hostCts.Token);

        using var conn = new IpcConnection(client);
        // sampleCount 12 == 3 chunks of 4, so chunk 1's prefetch arms after chunk 0 and chunk 1 is not last.
        var provider = new IpcSampleProvider(conn, buffers, sampleCount: 12, sampleRate: sampleRate);

        try
        {
            using (Pcm<Stereo32BitFloat> chunk0 = await provider.Sample(0, sampleRate))
                Assert.That(ReadSampleSignature(chunk0), Is.EqualTo(0L), "chunk 0 loads and arms the prefetch of chunk 1");

            Assert.That(async () => await provider.Sample(sampleRate, sampleRate),
                Throws.TypeOf<FFmpegWorkerException>(),
                "the faulted prefetch surfaces on the request that consumes chunk 1");

            using Pcm<Stereo32BitFloat> chunk1 = await provider.Sample(sampleRate, sampleRate);
            Assert.That(ReadSampleSignature(chunk1), Is.EqualTo(sampleRate),
                "the retry recovers because the faulted prefetch was not pinned to the provider");
        }
        finally
        {
            await StopHost(hostCts, server, hostTask);
            DisposeBuffers(buffers);
        }
    }

    [Test]
    public async Task Sample_WhenRequestStraddlesEof_ReturnsRequestedLengthWithSilenceTail()
    {
        // SampleCount aligns to a chunk boundary (8 == 2 * sampleRate 4). A request straddling EOF used to
        // load an empty next chunk and slice a 0-length span -> ArgumentOutOfRangeException. Per the
        // ISampleProvider convention (SampleProviderImpl), the fix must return a Pcm of the REQUESTED length
        // with the real samples up front and the past-EOF tail zero-filled (silence) — not a shortened Pcm,
        // which would leave stale samples in the encoder's fixed-size final frame.
        const long sampleRate = 4;
        var (server, client) = ConnectPair();
        var buffers = CreateBuffers();
        var hostCts = new CancellationTokenSource();
        var hostTask = RunSampleServingHost(server, buffers, hostCts.Token);

        using var conn = new IpcConnection(client);
        var provider = new IpcSampleProvider(conn, buffers, sampleCount: 8, sampleRate: sampleRate);

        try
        {
            // offset 4 (chunk-aligned), length 6 -> only 4 samples remain (4..8); the last 2 must be silent.
            Pcm<Stereo32BitFloat> result = null!;
            Assert.That(async () => result = await provider.Sample(4, 6), Throws.Nothing,
                "a request straddling EOF must zero-pad, not throw ArgumentOutOfRangeException");

            using (result)
            {
                Assert.That(result.NumSamples, Is.EqualTo(6),
                    "Sample must return the requested length (zero-padded), not a clamped Pcm");
                Assert.That(ReadSampleSignature(result), Is.EqualTo(4),
                    "the real prefix is preserved (chunk-4 signature lands at sample 0)");

                // The 2 samples past SampleCount (indices 4,5) must be zero-filled silence, not stale data.
                byte[] all = new byte[result.NumSamples * Stereo32BitFloatBytesPerSample];
                Marshal.Copy(result.Data, all, 0, all.Length);
                byte[] tail = all[(4 * Stereo32BitFloatBytesPerSample)..];
                Assert.That(tail, Has.All.EqualTo((byte)0),
                    "samples past the timeline end must be silence, not stale frame data");
            }
        }
        finally
        {
            await StopHost(hostCts, server, hostTask);
            DisposeBuffers(buffers);
        }
    }
}
