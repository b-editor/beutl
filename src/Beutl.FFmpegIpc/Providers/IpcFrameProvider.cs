using System.Collections.Concurrent;

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
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;
    private readonly PrefetchSlot<long, IpcMessage> _prefetch = new();
    private int _bufferIndex;
    private volatile bool _disposed;

    public IpcFrameProvider(IpcConnection connection, SharedMemoryBuffer[] videoBuffers,
        long frameCount, Rational frameRate, int sourceWidth, int sourceHeight)
    {
        _connection = connection;
        _videoBuffers = videoBuffers;
        _sourceWidth = sourceWidth;
        _sourceHeight = sourceHeight;
        FrameCount = frameCount;
        FrameRate = frameRate;
    }

    public long FrameCount { get; }
    public Rational FrameRate { get; }
    // Test-only probe: no production code reads it.
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
            Task<IpcMessage>? stale = _prefetch.Detach();
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
        var frameInfo = response.RequirePayload<ProvideFrameMessage>(MessageType.ProvideFrame);

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
        // SharedMemoryBuffer.Read checks only the shared buffer, so reject a frame that would overrun the bitmap.
        if (frameInfo.Width <= 0 || frameInfo.Height <= 0)
            throw new InvalidOperationException(
                $"Frame has non-positive dimensions {frameInfo.Width}x{frameInfo.Height}.");

        // The encoder copies each frame into a MediaFrame fixed at the source size, which a larger frame would overrun.
        if (frameInfo.Width != _sourceWidth || frameInfo.Height != _sourceHeight)
            throw new InvalidOperationException(
                $"Frame dimensions {frameInfo.Width}x{frameInfo.Height} do not match the negotiated source " +
                $"size {_sourceWidth}x{_sourceHeight}.");

        (BitmapColorType colorType, BitmapColorSpace colorSpace, int bytesPerPixel) = GetFrameFormat(frameInfo);
        long expected = (long)frameInfo.Width * frameInfo.Height * bytesPerPixel;
        if (frameInfo.DataLength != expected)
            throw new InvalidOperationException(
                $"Frame DataLength {frameInfo.DataLength} does not match the {frameInfo.Width}x{frameInfo.Height} " +
                $"{colorType} buffer size {expected}.");

        // Reject a frame larger than the shared buffer before allocating a native bitmap that Read would reject.
        if (expected > _videoBuffers[bufferIndex].Capacity)
            throw new InvalidOperationException(
                $"Frame size {expected} exceeds the shared buffer capacity {_videoBuffers[bufferIndex].Capacity}.");

        var alphaType = frameInfo.Premul ? BitmapAlphaType.Premul : BitmapAlphaType.Unpremul;
        var bmp = new Bitmap(frameInfo.Width, frameInfo.Height, colorType, alphaType, colorSpace);

        try
        {
            // Read into the bitmap's own span, and dispose the bitmap if Read throws so it doesn't leak.
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
            // Derive bytes-per-pixel from the color type itself, never the payload: trusting the
            // payload's BytesPerPixel here would let a peer underreport it, pass the DataLength /
            // Capacity guards, yet allocate/read a wider BitmapColorType.
            return (colorType, ColorSpaceFor(colorType), BytesPerPixelOf(colorType));
        }

        return frameInfo.BytesPerPixel switch
        {
            Bgra8888BytesPerPixel => (BitmapColorType.Bgra8888, BitmapColorSpace.Srgb, Bgra8888BytesPerPixel),
            RgbaF16BytesPerPixel => (BitmapColorType.RgbaF16, BitmapColorSpace.LinearSrgb, RgbaF16BytesPerPixel),
            _ => throw new InvalidOperationException(
                $"Unsupported frame BytesPerPixel {frameInfo.BytesPerPixel}."),
        };
    }

    private static readonly ConcurrentDictionary<BitmapColorType, int> s_bytesPerPixelCache = new();

    private static int BytesPerPixelOf(BitmapColorType colorType)
        => s_bytesPerPixelCache.GetOrAdd(colorType, static ct =>
        {
            using var probe = new Bitmap(1, 1, ct, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            return probe.BytesPerPixel;
        });

    private static BitmapColorSpace ColorSpaceFor(BitmapColorType colorType)
    {
        // Beutl's render-target frames are linear float; integer formats (SDR and 16-bit integer
        // HDR alike) are sRGB-encoded.
        return colorType is BitmapColorType.RgbaF16 or BitmapColorType.RgbaF16Clamped
            or BitmapColorType.RgbaF32 or BitmapColorType.AlphaF16 or BitmapColorType.RgF16
            or BitmapColorType.RgbF16F16F16x or BitmapColorType.RF16
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

        // Don't wait on a prefetch whose connection may be closing; only observe its fault. Unlike a sample
        // prefetch, a successful frame prefetch holds nothing to release.
        _prefetch.Detach()?.ContinueWith(
            static t => { _ = t.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }
}
