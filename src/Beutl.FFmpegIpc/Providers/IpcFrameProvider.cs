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

    // RgbaF16 destination: 4 channels * 2 bytes. Must match the BitmapColorType.RgbaF16 used below.
    private const int RgbaF16BytesPerPixel = 8;

    private Bitmap BuildBitmap(ProvideFrameMessage frameInfo, int bufferIndex)
    {
        // SharedMemoryBuffer.Read bounds-checks the shared buffer, not the destination bitmap, so a
        // worker-reported frame that doesn't match the RgbaF16 destination must be rejected before it
        // overruns the bitmap. Dimensions are checked first so a non-positive size can't pass the
        // DataLength check with a degenerate (zero) length.
        if (frameInfo.Width <= 0 || frameInfo.Height <= 0)
            throw new InvalidOperationException(
                $"Frame has non-positive dimensions {frameInfo.Width}x{frameInfo.Height}.");

        long expected = (long)frameInfo.Width * frameInfo.Height * RgbaF16BytesPerPixel;
        if (frameInfo.DataLength != expected)
            throw new InvalidOperationException(
                $"Frame DataLength {frameInfo.DataLength} does not match the {frameInfo.Width}x{frameInfo.Height} " +
                $"RgbaF16 buffer size {expected}.");

        var alphaType = frameInfo.Premul ? BitmapAlphaType.Premul : BitmapAlphaType.Unpremul;
        var bmp = new Bitmap(frameInfo.Width, frameInfo.Height, BitmapColorType.RgbaF16, alphaType, BitmapColorSpace.LinearSrgb);

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
