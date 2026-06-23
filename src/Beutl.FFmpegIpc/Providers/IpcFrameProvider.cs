using System.Diagnostics;
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

    // Prefetch: request the next frame's rendering ahead of time.
    private Task<IpcMessage>? _prefetchTask;
    private int _prefetchBufferIndex;
    private long _prefetchFrameIndex;

    // Last returned frame. A repeat request rebuilds from this buffer, which the in-flight prefetch
    // never overwrites (prefetch always targets the opposite slot, 1 - _lastReadBufferIndex).
    private long _lastFrame = -1;
    private int _lastReadBufferIndex;
    private ProvideFrameMessage? _lastFrameInfo;

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
        // Same-frame re-request (paused-preview / same-frame scrub): leave the in-flight prefetch
        // untouched so a later advance still consumes it.
        if (frame == _lastFrame && _lastFrameInfo != null)
        {
            Debug.Assert(_prefetchTask == null || _prefetchBufferIndex != _lastReadBufferIndex,
                "an in-flight prefetch must target the opposite slot, else the retained buffer could be overwritten");
            FramesRendered++;
            return BuildBitmap(_lastFrameInfo, _lastReadBufferIndex);
        }

        // Invalidate the retained frame before any IPC, so a failure below cannot be masked by a
        // same-frame retry serving the previously cached frame.
        _lastFrame = -1;

        IpcMessage response;
        int readBufferIndex;

        if (_prefetchTask != null && _prefetchFrameIndex == frame)
        {
            // Prefetch already in flight for the requested frame: use its result.
            response = await _prefetchTask;
            readBufferIndex = _prefetchBufferIndex;
            _prefetchTask = null;
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

            // First frame or non-sequential request: fetch the requested frame afresh.
            readBufferIndex = _bufferIndex;
            var request = IpcMessage.Create(_connection.NextId(), MessageType.RequestFrame,
                new RequestFrameMessage { FrameIndex = frame, BufferIndex = readBufferIndex });
            response = await _connection.SendAndReceiveAsync(request)
                       ?? throw new IOException("Connection closed while waiting for frame");
        }

        if (response.Type == MessageType.CancelEncode)
            throw new OperationCanceledException();

        if (response.Error != null)
            throw new InvalidOperationException($"Frame render failed: {response.Error}");

        var frameInfo = response.GetPayload<ProvideFrameMessage>()
            ?? throw new InvalidOperationException("Missing payload for ProvideFrame");

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

        _lastFrame = frame;
        _lastReadBufferIndex = readBufferIndex;
        _lastFrameInfo = frameInfo;

        Bitmap bmp = BuildBitmap(frameInfo, readBufferIndex);
        _bufferIndex = 1 - readBufferIndex;
        FramesRendered++;
        return bmp;
    }

    private Bitmap BuildBitmap(ProvideFrameMessage frameInfo, int bufferIndex)
    {
        var alphaType = frameInfo.Premul ? BitmapAlphaType.Premul : BitmapAlphaType.Unpremul;
        var bmp = new Bitmap(frameInfo.Width, frameInfo.Height, BitmapColorType.RgbaF16, alphaType, BitmapColorSpace.LinearSrgb);

        unsafe
        {
            _videoBuffers[bufferIndex].Read(new Span<byte>((void*)bmp.Data, frameInfo.DataLength));
        }

        return bmp;
    }
}
