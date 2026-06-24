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
    private int _bufferIndex;

    private Task<IpcMessage>? _prefetchTask;
    private int _prefetchBufferIndex;
    private long _prefetchFrameIndex;
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
        IpcMessage response;
        int readBufferIndex;

        if (_prefetchTask != null && _prefetchFrameIndex == frame)
        {
            // Prefetch already in flight for the requested frame. Clear the field before awaiting (like
            // the stale-drain branch) so a faulted prefetch can't pin the provider to a re-throwing task.
            Task<IpcMessage> prefetchTask = _prefetchTask;
            _prefetchTask = null;
            readBufferIndex = _prefetchBufferIndex;
            response = await prefetchTask;
        }
        else
        {
            // Prefetch in flight but for a different frame (seek / non-sequential request). On a
            // non-multiplexed connection responses are read in send order, so the stale prefetch
            // response must be awaited and discarded before issuing the fresh request; otherwise that
            // request would read this old response and hit an id mismatch.
            if (_prefetchTask != null)
            {
                Task<IpcMessage> staleTask = _prefetchTask;
                _prefetchTask = null;
                await staleTask;
            }

            readBufferIndex = _bufferIndex;
            var request = IpcMessage.Create(_connection.NextId(), MessageType.RequestFrame,
                new RequestFrameMessage { FrameIndex = frame, BufferIndex = readBufferIndex });
            response = await _connection.SendAndReceiveAsync(request);
        }

        // SendAndReceiveAsync already surfaces a closed connection as IOException and an error response as
        // FFmpegWorkerException, so the response here is non-null and error-free; only CancelEncode (a live
        // non-error response) still needs handling.
        if (response.Type == MessageType.CancelEncode)
            throw new OperationCanceledException();

        var frameInfo = response.GetPayload<ProvideFrameMessage>()
            ?? throw new InvalidOperationException("Missing payload for ProvideFrame");

        // Validate the current frame before arming the prefetch so a mismatched DataLength can't leave
        // an unobserved _prefetchTask in flight nor mask the failure behind an extra RequestFrame.
        Bitmap bmp = BuildBitmap(frameInfo, readBufferIndex);

        // Prefetch the next frame into the opposite slot (double buffering).
        long nextFrame = frame + 1;
        if (nextFrame < FrameCount)
        {
            _prefetchBufferIndex = 1 - readBufferIndex;
            _prefetchFrameIndex = nextFrame;
            var nextRequest = IpcMessage.Create(_connection.NextId(), MessageType.RequestFrame,
                new RequestFrameMessage { FrameIndex = nextFrame, BufferIndex = _prefetchBufferIndex });
            _prefetchTask = _connection.SendAndReceiveAsync(nextRequest).AsTask();
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
    internal bool IsPrefetchFaultedForTest() => _prefetchTask?.IsFaulted == true;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // A prefetch may still be in flight when the encode is torn down (cancel, error, normal end). The
        // connection/pipe it talks to may already be closing, so we must NOT synchronously wait on it here:
        // .Wait()/.GetAwaiter().GetResult() could deadlock or rethrow the pipe-teardown fault. Instead attach
        // a fault-swallowing continuation so any fault is observed (preventing UnobservedTaskException) and
        // return promptly. Owning the connection is the caller's job, so we only neutralize our own task.
        _prefetchTask?.ContinueWith(
            static t => { _ = t.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
        _prefetchTask = null;
    }
}
