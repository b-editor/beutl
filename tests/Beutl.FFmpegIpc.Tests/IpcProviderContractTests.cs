using System.IO.Pipes;
using System.Reflection;
using System.Runtime.CompilerServices;
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
using static Beutl.FFmpegIpc.Tests.PipeTestHelpers;

namespace Beutl.FFmpegIpc.Tests;

/// <summary>
/// Pins the IPC client-side providers against a fake host driven over a real NamedPipe. The providers use the
/// non-multiplexed <see cref="IpcConnection.SendAndReceiveAsync"/> path, so the host answers requests sequentially.
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

    private const string PipePrefix = "beutl-ipc-prov-";

    private static string NewName() => PipePrefix + Guid.NewGuid().ToString("N")[..8];

    private static SharedMemoryBuffer[] CreateBuffers() =>
        [SharedMemoryBuffer.Create(NewName(), BufferCapacity), SharedMemoryBuffer.Create(NewName(), BufferCapacity)];

    private static long ReadFrameSignature(Bitmap bmp)
    {
        byte[] buf = new byte[FrameDataLength];
        Marshal.Copy(bmp.Data, buf, 0, FrameDataLength);
        return BitConverter.ToInt64(buf);
    }

    /// <summary>The fake host's answer to a request, after writing to the buffers what the answer points at.</summary>
    private delegate IpcMessage HostResponse(IpcMessage request, SharedMemoryBuffer[] buffers);

    /// <summary>
    /// A connected pipe pair, the shared buffers and a fake host that answers each request it reads with
    /// <c>respond</c>, one at a time as the providers' non-multiplexed connection expects. Disposing stops the
    /// host, then frees the buffers and the connection.
    /// </summary>
    private sealed class FakeHost : IAsyncDisposable
    {
        private readonly NamedPipeServerStream _server;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _task;

        public FakeHost(HostResponse respond)
        {
            (_server, NamedPipeClientStream client) = ConnectPair(PipePrefix);
            Buffers = CreateBuffers();
            _task = Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    var req = await MessageSerializer.ReadMessageAsync(_server, _cts.Token);
                    if (req == null)
                        return;

                    await MessageSerializer.WriteMessageAsync(_server, respond(req, Buffers), _cts.Token);
                }
            }, _cts.Token);
            Connection = new IpcConnection(client);
        }

        public SharedMemoryBuffer[] Buffers { get; }

        public IpcConnection Connection { get; }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _server.Dispose();
            try { await _task; }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }

            foreach (var buffer in Buffers)
                buffer.Dispose();
            Connection.Dispose();
        }
    }

    private static IpcMessage ProvideFrame(
        IpcMessage request, int width, int height, int dataLength, int colorType = (int)BitmapColorType.RgbaF16)
    {
        return IpcMessage.Create(request.Id, MessageType.ProvideFrame, new ProvideFrameMessage
        {
            Width = width,
            Height = height,
            DataLength = dataLength,
            Premul = false,
            ColorType = colorType,
        });
    }

    // Serves the requested frame by writing its index, as the frame's signature, into the requested buffer slot.
    private static IpcMessage ServeFrame(IpcMessage request, SharedMemoryBuffer[] buffers)
    {
        var payload = request.GetPayload<RequestFrameMessage>()!;
        buffers[payload.BufferIndex].Write(BitConverter.GetBytes(payload.FrameIndex));
        return ProvideFrame(request, width: 1, height: 1, FrameDataLength);
    }

    // Serves each RequestFrame and records the received frame indices in order.
    private static HostResponse FrameServingHost(List<long> received, object gate)
    {
        return (request, buffers) =>
        {
            lock (gate)
                received.Add(request.GetPayload<RequestFrameMessage>()!.FrameIndex);
            return ServeFrame(request, buffers);
        };
    }

    // Replies to every request with one fixed ProvideFrame, so a test can drive the
    // provider's frame-validation guards with specific Width/Height/DataLength values.
    private static HostResponse MalformedFrameHost(
        int width, int height, int dataLength, int colorType = (int)BitmapColorType.RgbaF16)
    {
        return (request, _) => ProvideFrame(request, width, height, dataLength, colorType);
    }

    // Like the real host, answers with a CancelEncode whose id differs from the request's.
    private static HostResponse FreshIdCancelingHost()
    {
        // req.Id + 1_000_000 is guaranteed distinct from the in-flight request id, mirroring a
        // CancelEncode minted from a separate id sequence on the host side.
        return (request, _) => IpcMessage.CreateSimple(request.Id + 1_000_000, MessageType.CancelEncode);
    }

    // Serves frame 0 with a mismatched DataLength (failing the destination-size guard)
    // but answers every other frame normally. Records received frame indices so a test can assert that
    // a rejected frame did not leave a stray prefetch request behind.
    private static HostResponse Frame0BadDataLengthHost(List<long> received, object gate)
    {
        return (request, buffers) =>
        {
            long frameIndex = request.GetPayload<RequestFrameMessage>()!.FrameIndex;
            lock (gate)
                received.Add(frameIndex);

            // Oversized DataLength would overrun the 1x1 RgbaF16 destination if not rejected.
            return frameIndex == 0
                ? ProvideFrame(request, width: 1, height: 1, FrameDataLength * 2)
                : ServeFrame(request, buffers);
        };
    }

    // Fails the first request for frame 1 with an error response (so the provider's
    // prefetch task faults) and answers every other request — including the second frame 1 request —
    // normally. Lets a test prove a faulted prefetch is not pinned and a retry recovers.
    private static HostResponse PrefetchFaultsOnceHost()
    {
        bool frame1Faulted = false;
        return (request, buffers) =>
        {
            if (request.GetPayload<RequestFrameMessage>()!.FrameIndex == 1 && !frame1Faulted)
            {
                frame1Faulted = true;
                return IpcMessage.CreateError(request.Id, "injected prefetch failure");
            }

            return ServeFrame(request, buffers);
        };
    }

    // Always answers the request for a specific frame index with an error response (so that
    // frame's prefetch faults) and serves every other frame normally. Lets a test prove that draining a
    // FAULTED stale prefetch after a seek discards the worker error instead of aborting the fresh request.
    private static HostResponse FrameErrorsOnIndexHost(long erroringFrameIndex)
    {
        return (request, buffers) => request.GetPayload<RequestFrameMessage>()!.FrameIndex == erroringFrameIndex
            ? IpcMessage.CreateError(request.Id, "injected stale-prefetch failure")
            : ServeFrame(request, buffers);
    }

    [Test]
    public async Task RenderFrame_SeekAfterPrefetch_ReturnsRequestedFrameAndIssuesFreshRequest()
    {
        // RenderFrame(0) arms a prefetch of frame 1. A seek to frame 5 must drain that stale prefetch
        // and issue a fresh RequestFrame{5}, returning frame 5's data — not the prefetched frame 1.
        var received = new List<long>();
        var gate = new object();
        await using var host = new FakeHost(FrameServingHost(received, gate));
        // frameCount: 6 makes frame 5 the last frame, so RenderFrame(5) issues no post-seek prefetch.
        // This keeps the test self-contained: no dangling prefetch task survives into teardown to
        // raise an unobserved exception when the pipe/buffers are disposed.
        var provider = new IpcFrameProvider(host.Connection, host.Buffers, frameCount: 6, frameRate: new Rational(30, 1), sourceWidth: 1, sourceHeight: 1);

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

    [Test]
    public async Task RenderFrame_WhenWorkerReportsBgra8888Frame_ReturnsBgra8888Bitmap()
    {
        await using var host = new FakeHost(MalformedFrameHost(
            width: 1, height: 1, dataLength: Bgra8888BytesPerPixel,
            colorType: (int)BitmapColorType.Bgra8888));
        var provider = new IpcFrameProvider(host.Connection, host.Buffers, frameCount: 1, frameRate: new Rational(30, 1), sourceWidth: 1, sourceHeight: 1);

        using Bitmap bitmap = await provider.RenderFrame(0);

        Assert.Multiple(() =>
        {
            Assert.That(bitmap.ColorType, Is.EqualTo(BitmapColorType.Bgra8888));
            Assert.That(bitmap.ColorSpace, Is.EqualTo(BitmapColorSpace.Srgb));
            Assert.That(bitmap.BytesPerPixel, Is.EqualTo(Bgra8888BytesPerPixel));
            Assert.That(bitmap.ByteCount, Is.EqualTo(Bgra8888BytesPerPixel));
        });
    }

    // Two 8-byte formats share the same byte width; the color type must be honored so an
    // integer Rgba16161616 frame is not rebuilt as half-float RgbaF16 (which would corrupt colors).
    [Test]
    public async Task RenderFrame_WhenWorkerReportsRgba16161616_KeepsTheIntegerFormat()
    {
        await using var host = new FakeHost(MalformedFrameHost(
            width: 1, height: 1, dataLength: RgbaF16BytesPerPixel,
            colorType: (int)BitmapColorType.Rgba16161616));
        var provider = new IpcFrameProvider(host.Connection, host.Buffers, frameCount: 1, frameRate: new Rational(30, 1), sourceWidth: 1, sourceHeight: 1);

        using Bitmap bitmap = await provider.RenderFrame(0);

        Assert.Multiple(() =>
        {
            Assert.That(bitmap.ColorType, Is.EqualTo(BitmapColorType.Rgba16161616));
            Assert.That(bitmap.ColorSpace, Is.EqualTo(BitmapColorSpace.Srgb));
        });
    }

    // Float frames are Beutl's linear render-target output whatever their channel layout, while integer frames, HDR
    // ones included, are sRGB-encoded, so the color space has to follow the explicit color type.
    [TestCase(BitmapColorType.RgbaF16, 8, true)]
    [TestCase(BitmapColorType.RgF16, 4, true)]
    [TestCase(BitmapColorType.RgbF16F16F16x, 8, true)]
    [TestCase(BitmapColorType.RF16, 2, true)]
    [TestCase(BitmapColorType.R16Unorm, 2, false)]
    [TestCase(BitmapColorType.Bgra10101010XR, 8, false)]
    public async Task RenderFrame_WhenWorkerReportsExplicitColorType_ReadsFloatFramesAsLinear(
        BitmapColorType colorType, int bytesPerPixel, bool linear)
    {
        await using var host = new FakeHost(MalformedFrameHost(
            width: 1, height: 1, dataLength: bytesPerPixel, colorType: (int)colorType));
        var provider = new IpcFrameProvider(host.Connection, host.Buffers, frameCount: 1, frameRate: new Rational(30, 1), sourceWidth: 1, sourceHeight: 1);

        using Bitmap bitmap = await provider.RenderFrame(0);

        Assert.Multiple(() =>
        {
            Assert.That(bitmap.ColorType, Is.EqualTo(colorType));
            Assert.That(bitmap.ColorSpace,
                Is.EqualTo(linear ? BitmapColorSpace.LinearSrgb : BitmapColorSpace.Srgb));
        });
    }

    // The DataLength/Capacity guards use the color type's width: a 4-byte DataLength for an 8-byte
    // Rgba16161616 frame must be rejected, not allocate/read a wider bitmap.
    [Test]
    public async Task RenderFrame_DataLengthNarrowerThanTheColorType_Throws()
    {
        await using var host = new FakeHost(MalformedFrameHost(
            width: 1, height: 1, dataLength: Bgra8888BytesPerPixel,
            colorType: (int)BitmapColorType.Rgba16161616));
        var provider = new IpcFrameProvider(host.Connection, host.Buffers, frameCount: 1, frameRate: new Rational(30, 1), sourceWidth: 1, sourceHeight: 1);

        Assert.That(async () => await provider.RenderFrame(0),
            Throws.TypeOf<InvalidOperationException>(),
            "the color type's real 8 bytes/pixel must be used, so a 4-byte DataLength is a mismatch");
    }

    [TestCase(FrameDataLength * 2)]
    [TestCase(FrameDataLength / 2)]
    [TestCase(0)]
    public async Task RenderFrame_WhenWorkerReportsMismatchedDataLength_Throws(int dataLength)
    {
        await using var host = new FakeHost(MalformedFrameHost(width: 1, height: 1, dataLength: dataLength));
        // frameCount 1 makes frame 0 the last frame, so no prefetch is armed and nothing dangles.
        var provider = new IpcFrameProvider(host.Connection, host.Buffers, frameCount: 1, frameRate: new Rational(30, 1), sourceWidth: 1, sourceHeight: 1);

        Assert.That(async () => await provider.RenderFrame(0),
            Throws.TypeOf<InvalidOperationException>(),
            "a DataLength that does not match the bitmap allocation must be rejected, not copied");
    }

    // The color type is no longer inferred from the byte width, so a frame without a valid one is refused.
    [TestCase(-1)]
    [TestCase(999)]
    public async Task RenderFrame_WhenFrameHasNoValidColorType_Throws(int colorType)
    {
        await using var host = new FakeHost(MalformedFrameHost(
            width: 1, height: 1, dataLength: Bgra8888BytesPerPixel, colorType: colorType));
        var provider = new IpcFrameProvider(host.Connection, host.Buffers, frameCount: 1, frameRate: new Rational(30, 1), sourceWidth: 1, sourceHeight: 1);

        Assert.That(async () => await provider.RenderFrame(0),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("no valid color type"));
    }

    [TestCase(0, 1)]
    [TestCase(1, 0)]
    public async Task RenderFrame_WhenFrameDimensionsAreNonPositive_Throws(int width, int height)
    {
        // DataLength 0 matches a zero-area frame's expected size, so only an explicit dimension guard
        // rejects it — without one the provider would hand back an empty bitmap instead of throwing.
        await using var host = new FakeHost(MalformedFrameHost(width, height, dataLength: 0));
        var provider = new IpcFrameProvider(host.Connection, host.Buffers, frameCount: 1, frameRate: new Rational(30, 1), sourceWidth: 1, sourceHeight: 1);

        Assert.That(async () => await provider.RenderFrame(0),
            Throws.TypeOf<InvalidOperationException>(),
            "a frame with a non-positive dimension must be rejected, not turned into an empty bitmap");
    }

    [Test]
    public async Task RenderFrame_WhenFrameExceedsBufferCapacity_ThrowsBeforeAllocating()
    {
        // 32x32 RgbaF16 = 8192 bytes > BufferCapacity (4096). The DataLength guard passes (8192 == the
        // 32x32 RgbaF16 size), so the capacity guard must reject the frame BEFORE the native bitmap is
        // allocated — otherwise a malformed/huge frame would trigger the large allocation only to fail
        // later in Read.
        const int oversizedDataLength = 32 * 32 * RgbaF16BytesPerPixel; // 8192 > BufferCapacity 4096
        await using var host = new FakeHost(MalformedFrameHost(width: 32, height: 32, dataLength: oversizedDataLength));
        // Negotiated source is 32x32 so the frame passes the dimension guard and the capacity guard is
        // what rejects it (the 32x32 RgbaF16 frame exceeds the 4096-byte buffer).
        var provider = new IpcFrameProvider(
            host.Connection, host.Buffers, frameCount: 1, frameRate: new Rational(30, 1), sourceWidth: 32, sourceHeight: 32);

        Assert.That(async () => await provider.RenderFrame(0),
            Throws.TypeOf<InvalidOperationException>(),
            "a frame larger than the shared buffer must be rejected by the pre-allocation capacity guard");

        // The rejected render must not count as a completed frame (FramesRendered++ runs only
        // after BuildBitmap succeeds).
        Assert.That(provider.FramesRendered, Is.EqualTo(0),
            "a render rejected before allocation must not count as a completed frame");
    }

    [Test]
    public async Task RenderFrame_WhenFrameDimensionsExceedNegotiatedSource_Throws()
    {
        // A 2x1 BGRA frame clears the DataLength and capacity guards but would overrun the encoder's 1x1 MediaFrame.
        await using var host = new FakeHost(MalformedFrameHost(
            width: 2, height: 1, dataLength: 2 * 1 * Bgra8888BytesPerPixel,
            colorType: (int)BitmapColorType.Bgra8888));
        var provider = new IpcFrameProvider(
            host.Connection, host.Buffers, frameCount: 1, frameRate: new Rational(30, 1), sourceWidth: 1, sourceHeight: 1);

        Assert.That(async () => await provider.RenderFrame(0),
            Throws.TypeOf<InvalidOperationException>(),
            "a frame with more pixels than the negotiated source must be rejected, not copied into the fixed frame");
        Assert.That(provider.FramesRendered, Is.EqualTo(0));
    }

    [Test]
    public async Task RenderFrame_WhenValidationFails_DoesNotArmPrefetch()
    {
        // A frame that fails the DataLength guard must not arm a prefetch of the next frame. With frame 0
        // rejected, a later RenderFrame(2) leaves the host having seen only [0, 2] — no stray prefetch 1.
        var received = new List<long>();
        var gate = new object();
        await using var host = new FakeHost(Frame0BadDataLengthHost(received, gate));
        // frameCount 3: frame 0 is not the last frame, so a misplaced guard *would* arm a prefetch of 1.
        // frame 2 is the last frame, so the successful render issues no further prefetch and nothing dangles.
        var provider = new IpcFrameProvider(host.Connection, host.Buffers, frameCount: 3, frameRate: new Rational(30, 1), sourceWidth: 1, sourceHeight: 1);

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

    [Test]
    public async Task RenderFrame_WhenPrefetchFaults_RecoversOnRetry()
    {
        // A faulted prefetch must not pin the provider. RenderFrame(0) arms a prefetch of frame 1; the
        // host faults it, so the matching RenderFrame(1) throws — but a retry must issue a fresh request
        // and recover instead of re-throwing the pinned faulted task forever.
        await using var host = new FakeHost(PrefetchFaultsOnceHost());
        // frameCount 2: frame 0 arms the prefetch of 1; the recovered frame 1 is last, so nothing dangles.
        var provider = new IpcFrameProvider(host.Connection, host.Buffers, frameCount: 2, frameRate: new Rational(30, 1), sourceWidth: 1, sourceHeight: 1);

        using Bitmap frame0 = await provider.RenderFrame(0);
        Assert.That(ReadFrameSignature(frame0), Is.EqualTo(0L), "frame 0 renders before the prefetch faults");

        Assert.That(async () => await provider.RenderFrame(1),
            Throws.TypeOf<FFmpegWorkerException>(),
            "the faulted prefetch surfaces on the matching request");

        using Bitmap frame1 = await provider.RenderFrame(1);
        Assert.That(ReadFrameSignature(frame1), Is.EqualTo(1L),
            "the retry recovers because the faulted prefetch was not pinned to the provider");
    }

    [Test]
    public async Task RenderFrame_SeekAfterPrefetchFaulted_DiscardsStaleErrorAndReturnsRequestedFrame()
    {
        // RenderFrame(0) arms a prefetch of frame 1, which the host faults with a worker error. A seek to
        // frame 5 must DRAIN that faulted stale prefetch, discard its error (the error belongs to the
        // discarded frame 1), then issue a fresh RequestFrame{5} and return frame 5 — not abort the seek by
        // re-throwing the stale prefetch's FFmpegWorkerException.
        await using var host = new FakeHost(FrameErrorsOnIndexHost(erroringFrameIndex: 1));
        // frameCount 6 makes frame 5 the last frame, so the recovered seek issues no further prefetch and
        // nothing dangles into teardown.
        var provider = new IpcFrameProvider(host.Connection, host.Buffers, frameCount: 6, frameRate: new Rational(30, 1), sourceWidth: 1, sourceHeight: 1);

        using Bitmap frame0 = await provider.RenderFrame(0);
        Assert.That(ReadFrameSignature(frame0), Is.EqualTo(0L), "frame 0 renders and arms the prefetch of frame 1");

        // Wait until the prefetch actually faults so the seek drains an already-faulted stale task.
        await WaitUntil(() => provider.IsPrefetchFaultedForTest(),
            TimeSpan.FromSeconds(5), "the in-flight frame prefetch faults");

        using Bitmap frame5 = await provider.RenderFrame(5);
        Assert.That(ReadFrameSignature(frame5), Is.EqualTo(5L),
            "the seek discards the faulted stale prefetch's worker error and returns the freshly requested frame 5");
    }

    // Captures every UnobservedTaskException raised on the finalizer thread while subscribed, so a test
    // can prove a provider's Dispose drained its in-flight prefetch instead of leaking it to the GC.
    private sealed class UnobservedTaskExceptionWatcher : IDisposable
    {
        private readonly List<Exception> _exceptions = [];
        private readonly object _gate = new();

        public UnobservedTaskExceptionWatcher()
        {
            // Unobserved tasks left by earlier tests surface on whichever GC runs next; flush them before
            // subscribing so the watcher only reports what the provider under test leaves behind.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.WaitForPendingFinalizers();
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
        await using var host = new FakeHost(PrefetchFaultsOnceHost());
        // frameCount 3: frame 0 arms a prefetch of frame 1, which the host faults. Frame 1 is never consumed.
        var provider = new IpcFrameProvider(host.Connection, host.Buffers, frameCount: 3, frameRate: new Rational(30, 1), sourceWidth: 1, sourceHeight: 1);

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

    [Test]
    public async Task Dispose_ObservesInFlightFaultedSamplePrefetch_NoUnobservedTaskException()
    {
        // Consuming chunk 0 arms a prefetch of chunk 1 (offset == sampleRate); the host faults it. We then
        // Dispose the provider WITHOUT consuming chunk 1, so the faulted sample prefetch is dropped by
        // Dispose. Its observing continuation must mark the fault so no UnobservedTaskException is raised,
        // and Dispose must also free the cached chunk.
        const long sampleRate = 4;
        using var watcher = new UnobservedTaskExceptionWatcher();
        await using var host = new FakeHost(SamplePrefetchFaultsOnceHost(sampleRate));
        // sampleCount 12 == 3 chunks of 4, so chunk 1's prefetch arms after chunk 0 and is not last.
        var provider = new IpcSampleProvider(host.Connection, host.Buffers, sampleCount: 12, sampleRate: sampleRate);

        using (Pcm<Stereo32BitFloat> chunk0 = (await provider.ReadNextSamples((int)sampleRate))!)
            Assert.That(ReadSampleSignature(chunk0), Is.EqualTo(0L), "chunk 0 loads and arms the prefetch of chunk 1");

        await WaitUntil(() => provider.IsPrefetchFaultedForTest(),
            TimeSpan.FromSeconds(5), "the in-flight sample prefetch faults");

        provider.Dispose();
        provider.Dispose(); // idempotent: a second Dispose must be a no-op.

        Exception[] unobserved = watcher.Drain();
        Assert.That(unobserved, Is.Empty,
            "Dispose must observe the dropped faulted sample prefetch so no UnobservedTaskException is raised");
    }

    [Test]
    public void ProviderDisposalFlags_AreVolatile()
    {
        foreach (Type providerType in new[] { typeof(IpcFrameProvider), typeof(IpcSampleProvider) })
        {
            FieldInfo field = providerType.GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new AssertionException($"{providerType.Name}._disposed was not found");

            Assert.That(field.GetRequiredCustomModifiers(), Does.Contain(typeof(IsVolatile)),
                $"{providerType.Name}._disposed must remain volatile");
        }
    }

    [Test]
    public async Task RenderFrame_AfterDispose_ThrowsObjectDisposedException()
    {
        await using var host = new FakeHost(ServeFrame);
        var provider = new IpcFrameProvider(host.Connection, host.Buffers, frameCount: 1, frameRate: new Rational(30, 1), sourceWidth: 1, sourceHeight: 1);

        provider.Dispose();
        Assert.That(async () => await provider.RenderFrame(0), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public async Task ReadNextSamples_AfterDispose_ThrowsObjectDisposedException()
    {
        await using var host = new FakeHost(ServeSample);
        var provider = new IpcSampleProvider(host.Connection, host.Buffers, sampleCount: 4, sampleRate: 4);

        provider.Dispose();
        Assert.That(async () => await provider.ReadNextSamples(4), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public async Task RenderFrame_WhenHostCancelsWithFreshId_ThrowsOperationCanceled()
    {
        // Regression test: a CancelEncode with a fresh id must be a cancel, not an ID mismatch, so that
        // HandleStartAsync reports "Cancelled".
        await using var host = new FakeHost(FreshIdCancelingHost());
        var provider = new IpcFrameProvider(host.Connection, host.Buffers, frameCount: 100, frameRate: new Rational(30, 1), sourceWidth: 1, sourceHeight: 1);

        Assert.That(async () => await provider.RenderFrame(0),
            Throws.TypeOf<OperationCanceledException>(),
            "a CancelEncode with a fresh id must surface as a clean cancel, not 'Response ID mismatch'");
    }

    [Test]
    public async Task ReadNextSamples_WhenHostCancelsWithFreshId_ThrowsOperationCanceled()
    {
        // Regression mirror of the frame-side test: a CancelEncode minted with a fresh id mid-encode must
        // surface from the sample path as OperationCanceledException, not "Response ID mismatch".
        await using var host = new FakeHost(FreshIdCancelingHost());
        var provider = new IpcSampleProvider(host.Connection, host.Buffers, sampleCount: 48000, sampleRate: 48000);

        Assert.That(async () => await provider.ReadNextSamples(1024),
            Throws.TypeOf<OperationCanceledException>(),
            "a CancelEncode with a fresh id must surface as a clean cancel, not 'Response ID mismatch'");
    }

    // ---- Sample-side host harness ----

    // Reads the first sample (8 bytes) the provider returned, interpreted as the chunk-offset signature.
    private static long ReadSampleSignature(Pcm<Stereo32BitFloat> pcm)
    {
        byte[] buf = new byte[Stereo32BitFloatBytesPerSample];
        Marshal.Copy(pcm.Data, buf, 0, Stereo32BitFloatBytesPerSample);
        return BitConverter.ToInt64(buf);
    }

    // Serves each RequestSample by writing that chunk's signature (its offset) into the
    // requested buffer slot and replying ProvideSample with a self-consistent NumSamples/DataLength.
    private static IpcMessage ServeSample(IpcMessage request, SharedMemoryBuffer[] buffers)
    {
        var payload = request.GetPayload<RequestSampleMessage>()!;
        int numSamples = (int)payload.Length;
        // Signature occupies the first 8 bytes (one Stereo32BitFloat sample); requires numSamples >= 1.
        buffers[payload.BufferIndex].Write(BitConverter.GetBytes(payload.Offset));

        return IpcMessage.Create(request.Id, MessageType.ProvideSample, new ProvideSampleMessage
        {
            NumSamples = numSamples,
            DataLength = numSamples * Stereo32BitFloatBytesPerSample,
        });
    }

    // Replies to every request with one fixed ProvideSample, so a test can drive the
    // provider's NumSamples/DataLength validation with specific values.
    private static HostResponse MalformedSampleHost(int numSamples, int dataLength)
    {
        return (request, _) => IpcMessage.Create(request.Id, MessageType.ProvideSample, new ProvideSampleMessage
        {
            NumSamples = numSamples,
            DataLength = dataLength,
        });
    }

    // Answers every request with one fixed error response, so a test can prove an injected
    // worker error surfaces as FFmpegWorkerException from the sample path (not InvalidOperationException).
    private static HostResponse ErroringSampleHost()
    {
        return (request, _) => IpcMessage.CreateError(request.Id, "injected sample failure");
    }

    // Fails the first request for the second chunk (offset == sampleRate) with an error
    // response (so the provider's prefetch task faults) and answers every other request — including the
    // retry of that chunk — normally. Lets a test prove a faulted sample prefetch is not pinned.
    private static HostResponse SamplePrefetchFaultsOnceHost(long sampleRate)
    {
        bool secondChunkFaulted = false;
        return (request, buffers) =>
        {
            if (request.GetPayload<RequestSampleMessage>()!.Offset == sampleRate && !secondChunkFaulted)
            {
                secondChunkFaulted = true;
                return IpcMessage.CreateError(request.Id, "injected sample prefetch failure");
            }

            return ServeSample(request, buffers);
        };
    }

    [TestCase(8 * Stereo32BitFloatBytesPerSample)]   // oversized: would overrun the native Pcm
    [TestCase(2 * Stereo32BitFloatBytesPerSample)]   // undersized vs NumSamples
    public async Task ReadNextSamples_WhenWorkerReportsMismatchedDataLength_Throws(int dataLength)
    {
        // The Pcm capacity is NumSamples * 8. A DataLength that disagrees with NumSamples must be rejected
        // before the Read, not used as the copy length (which would over/under-run the native allocation).
        await using var host = new FakeHost(MalformedSampleHost(numSamples: 4, dataLength));
        var provider = new IpcSampleProvider(host.Connection, host.Buffers, sampleCount: 4, sampleRate: 4);

        Assert.That(async () => await provider.ReadNextSamples(4),
            Throws.TypeOf<InvalidOperationException>(),
            "a DataLength that does not match NumSamples * 8 must be rejected, not used as the copy length");
    }

    [Test]
    public async Task ReadNextSamples_WhenHostErrors_ThrowsFFmpegWorkerException()
    {
        // An injected worker error must surface as FFmpegWorkerException (raised by SendAndReceiveAsync),
        // mirroring the frame-side guarantee — not be re-wrapped into InvalidOperationException.
        await using var host = new FakeHost(ErroringSampleHost());
        var provider = new IpcSampleProvider(host.Connection, host.Buffers, sampleCount: 4, sampleRate: 4);

        Assert.That(async () => await provider.ReadNextSamples(4),
            Throws.TypeOf<FFmpegWorkerException>(),
            "an injected worker error must surface as FFmpegWorkerException, not InvalidOperationException");
    }

    [Test]
    public async Task ReadNextSamples_WhenPrefetchFaults_RecoversOnRetry()
    {
        // A faulted sample prefetch must not pin the provider: the read of chunk 1 throws, and a retry requests it afresh.
        const long sampleRate = 4;
        await using var host = new FakeHost(SamplePrefetchFaultsOnceHost(sampleRate));
        // sampleCount 12 == 3 chunks of 4, so chunk 1's prefetch arms after chunk 0 and chunk 1 is not last.
        var provider = new IpcSampleProvider(host.Connection, host.Buffers, sampleCount: 12, sampleRate: sampleRate);

        using (Pcm<Stereo32BitFloat> chunk0 = (await provider.ReadNextSamples((int)sampleRate))!)
            Assert.That(ReadSampleSignature(chunk0), Is.EqualTo(0L), "chunk 0 loads and arms the prefetch of chunk 1");

        Assert.That(async () => await provider.ReadNextSamples((int)sampleRate),
            Throws.TypeOf<FFmpegWorkerException>(),
            "the faulted prefetch surfaces on the read that consumes chunk 1");

        using Pcm<Stereo32BitFloat> chunk1 = (await provider.ReadNextSamples((int)sampleRate))!;
        Assert.That(ReadSampleSignature(chunk1), Is.EqualTo(sampleRate),
            "the retry recovers because the faulted prefetch was not pinned to the provider");
    }

    [Test]
    public async Task ReadNextSamples_WhenRequestStraddlesEof_ReturnsRequestedLengthWithSilenceTail()
    {
        // Regression test: like SampleProviderImpl, a request straddling a chunk-aligned EOF returns the requested
        // length with the past-EOF tail silent, so the encoder's fixed-size final frame holds no stale samples.
        const long sampleRate = 4;
        await using var host = new FakeHost(ServeSample);
        var provider = new IpcSampleProvider(host.Connection, host.Buffers, sampleCount: 8, sampleRate: sampleRate);

        // offset 4 (chunk-aligned), length 6 -> only 4 samples remain (4..8); the last 2 must be silent.
        using (await provider.ReadNextSamples(4))
        {
        }

        Pcm<Stereo32BitFloat> result = null!;
        Assert.That(async () => result = (await provider.ReadNextSamples(6))!, Throws.Nothing,
            "a request straddling EOF must zero-pad, not throw ArgumentOutOfRangeException");
        Assert.That(await provider.ReadNextSamples(4), Is.Null, "every sample was read");

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

    [Test]
    public async Task ReadNextSamples_WhenSecondChunkLoadFaultsMidSplit_DisposesSplitBufferAndDoesNotLeak()
    {
        // A cross-chunk read allocates its result before loading the second chunk; when that load faults, as an
        // IPC error or cancellation mid-encode does, the result must be disposed rather than leaked.
        const long sampleRate = 4;
        await using var host = new FakeHost(SamplePrefetchFaultsOnceHost(sampleRate));
        // sampleCount 12 == 3 chunks of 4; after 2 samples, a read of 4 straddles the chunk 0/1 boundary.
        var provider = new IpcSampleProvider(host.Connection, host.Buffers, sampleCount: 12, sampleRate: sampleRate);
        Pcm<Stereo32BitFloat>? splitBuffer = null;
        provider.CrossChunkSplitAllocatedForTest = pcm => splitBuffer = pcm;

        try
        {
            using (await provider.ReadNextSamples(2))
            {
            }

            Assert.That(async () => await provider.ReadNextSamples(4),
                Throws.TypeOf<FFmpegWorkerException>(),
                "the second chunk load faults mid-split, surfacing the worker error");

            Assert.That(splitBuffer, Is.Not.Null,
                "the cross-chunk split path allocated result2 before the faulting second load");
            Assert.That(splitBuffer!.IsDisposed, Is.True,
                "result2 must be disposed on a mid-split fault so its native buffer is not leaked");
        }
        finally
        {
            provider.Dispose();
        }
    }
}
