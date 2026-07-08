using Beutl.Extensibility;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegIpc.Transport;
using Beutl.Media;

namespace Beutl.FFmpegIpc.Providers;

internal sealed class IpcFrameProvider : IFrameProvider
{
    private readonly IpcConnection _connection;
    private readonly SharedMemoryBuffer[] _videoBuffers;
    private readonly PrefetchSlot<long, IpcMessage> _prefetch = new();
    private int _bufferIndex;
    private bool _disposed;

    public IpcFrameProvider(IpcConnection connection, SharedMemoryBuffer[] videoBuffers,
        long frameCount, Rational frameRate)
    {
        _connection = connection;
        _videoBuffers = videoBuffers;
        FrameCount = frameCount;
        FrameRate = frameRate;
    }

    public long FrameCount { get; }
    public Rational FrameRate { get; }
    public long FramesRendered { get; private set; }

    public async ValueTask<Bitmap> RenderFrame(long frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        IpcMessage response;
        int readBufferIndex;

        Task<IpcMessage>? prefetched = _prefetch.TryConsumeMatching(frame, out readBufferIndex);
        if (prefetched != null)
        {
            response = await prefetched;
        }
        else
        {
            // Prefetch in flight but for a different frame (seek / non-sequential request): drain the stale
            // prefetch before issuing the fresh request so it can't be read in place of the new response.
            Task<IpcMessage>? stale = _prefetch.TryDetachStale(frame);
            if (stale != null)
            {
                try
                {
                    await stale;
                }
                catch (FFmpegWorkerException)
                {
                    // The drain has already consumed the stale prefetch's response off the pipe; its worker
                    // error belongs to the discarded frame, so it must not abort the fresh request below.
                }
            }

            readBufferIndex = _bufferIndex;
            var request = IpcMessage.Create(_connection.NextId(), MessageType.RequestFrame,
                new RequestFrameMessage { FrameIndex = frame, BufferIndex = readBufferIndex });
            response = await _connection.SendAndReceiveAsync(request);
        }

        // SendAndReceiveAsync surfaces a closed connection as IOException, an error response as
        // FFmpegWorkerException, and a host CancelEncode as OperationCanceledException, so the response here
        // is always a live ProvideFrame for this request.
        var frameInfo = response.GetPayload<ProvideFrameMessage>()
            ?? throw new InvalidOperationException("Missing payload for ProvideFrame");

        // Validate the current frame before arming the prefetch so a mismatched DataLength can't leave
        // an unobserved _prefetchTask in flight nor mask the failure behind an extra RequestFrame.
        Bitmap bmp = BuildBitmap(frameInfo, readBufferIndex);

        // Prefetch the next frame into the opposite slot (double buffering).
        long nextFrame = frame + 1;
        if (nextFrame < FrameCount)
        {
            int prefetchBufferIndex = 1 - readBufferIndex;
            var nextRequest = IpcMessage.Create(_connection.NextId(), MessageType.RequestFrame,
                new RequestFrameMessage { FrameIndex = nextFrame, BufferIndex = prefetchBufferIndex });
            _prefetch.Arm(nextFrame, prefetchBufferIndex, _connection.SendAndReceiveAsync(nextRequest).AsTask());
        }

        _bufferIndex = 1 - readBufferIndex;
        FramesRendered++;
        return bmp;
    }

    // The encoding IPC accepts the two formats produced by Beutl's frame providers:
    // SDR decoded frames are BGRA8888, while render-target frames are linear RgbaF16.
    private const int Bgra8888BytesPerPixel = 4;
    private const int RgbaF16BytesPerPixel = 8;

    private Bitmap BuildBitmap(ProvideFrameMessage frameInfo, int bufferIndex)
    {
        // SharedMemoryBuffer.Read bounds-checks the shared buffer, not the destination bitmap, so a
        // worker-reported frame that doesn't match the destination bitmap must be rejected before it
        // overruns the bitmap. Dimensions and format are checked first so invalid metadata can't pass
        // the DataLength check with a degenerate length.
        if (frameInfo.Width <= 0 || frameInfo.Height <= 0)
            throw new InvalidOperationException(
                $"Frame has non-positive dimensions {frameInfo.Width}x{frameInfo.Height}.");

        (BitmapColorType colorType, BitmapColorSpace colorSpace, int bytesPerPixel) = GetFrameFormat(frameInfo);
        long expected = (long)frameInfo.Width * frameInfo.Height * bytesPerPixel;
        if (frameInfo.DataLength != expected)
            throw new InvalidOperationException(
                $"Frame DataLength {frameInfo.DataLength} does not match the {frameInfo.Width}x{frameInfo.Height} " +
                $"{colorType} buffer size {expected}.");

        // Reject an oversized frame BEFORE allocating the native bitmap: the frame's pixel data must
        // fit the shared buffer it is read from, so a frame that cannot fit is invalid and would
        // otherwise trigger a huge native allocation that only fails later in Read.
        if (expected > _videoBuffers[bufferIndex].Capacity)
            throw new InvalidOperationException(
                $"Frame size {expected} exceeds the shared buffer capacity {_videoBuffers[bufferIndex].Capacity}.");

        var alphaType = frameInfo.Premul ? BitmapAlphaType.Premul : BitmapAlphaType.Unpremul;
        var bmp = new Bitmap(frameInfo.Width, frameInfo.Height, colorType, alphaType, colorSpace);

        try
        {
            // Read into the bitmap's own span so the copy length is its real ByteCount, not a recomputed
            // size. SharedMemoryBuffer.Read still bounds-checks against the shared-buffer Capacity (the
            // DataLength guard above does not), so a frame that exceeds Capacity throws here — dispose the
            // freshly allocated native bitmap before propagating so the throw can't leak it.
            _videoBuffers[bufferIndex].Read(bmp.GetPixelSpan());
            return bmp;
        }
        catch
        {
            bmp.Dispose();
            throw;
        }
    }

    private static (BitmapColorType ColorType, BitmapColorSpace ColorSpace, int BytesPerPixel) GetFrameFormat(
        ProvideFrameMessage frameInfo)
    {
        // An explicit color type disambiguates two frames of the same byte width (e.g. half-float
        // RgbaF16 vs integer Rgba16161616 at 8 bytes). Without it, fall back to inferring from the
        // byte width (older peers that do not send a color type).
        if (frameInfo.ColorType >= 0 && Enum.IsDefined((BitmapColorType)frameInfo.ColorType))
        {
            var colorType = (BitmapColorType)frameInfo.ColorType;
            return (colorType, ColorSpaceFor(colorType), frameInfo.BytesPerPixel);
        }

        return frameInfo.BytesPerPixel switch
        {
            Bgra8888BytesPerPixel => (BitmapColorType.Bgra8888, BitmapColorSpace.Srgb, Bgra8888BytesPerPixel),
            RgbaF16BytesPerPixel => (BitmapColorType.RgbaF16, BitmapColorSpace.LinearSrgb, RgbaF16BytesPerPixel),
            _ => throw new InvalidOperationException(
                $"Unsupported frame BytesPerPixel {frameInfo.BytesPerPixel}."),
        };
    }

    private static BitmapColorSpace ColorSpaceFor(BitmapColorType colorType)
    {
        // Beutl's render-target frames are linear float; integer formats (SDR and 16-bit integer
        // HDR alike) are sRGB-encoded.
        return colorType is BitmapColorType.RgbaF16 or BitmapColorType.RgbaF16Clamped
            or BitmapColorType.RgbaF32 or BitmapColorType.AlphaF16 or BitmapColorType.RgF16
            ? BitmapColorSpace.LinearSrgb
            : BitmapColorSpace.Srgb;
    }

    // Test-only probe: lets a Dispose test wait until the in-flight prefetch has actually faulted before
    // dropping it, so the faulted-task path is exercised deterministically without a sleep.
    internal bool IsPrefetchFaultedForTest() => _prefetch.IsFaulted;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // A prefetch may still be in flight when the encode is torn down (cancel, error, normal end). The
        // connection/pipe it talks to may already be closing, so we must NOT synchronously wait on it here:
        // .Wait()/.GetAwaiter().GetResult() could deadlock or rethrow the pipe-teardown fault. Instead attach
        // a fault-swallowing continuation so any fault is observed (preventing UnobservedTaskException) and
        // return promptly. Owning the connection is the caller's job, so we only neutralize our own task.
        // OnlyOnFaulted here, but not in IpcSampleProvider.Dispose: a frame prefetch yields a managed
        // IpcMessage with nothing to release (the Bitmap is built lazily only when a frame is consumed), so a
        // successful prefetch leaks nothing — whereas a sample prefetch yields a native Pcm its continuation
        // must dispose on the success path.
        _prefetch.Detach()?.ContinueWith(
            static t => { _ = t.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }
}
