using Beutl.Extensibility;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegIpc.Transport;
using Beutl.Media;

namespace Beutl.FFmpegWorker.Providers;

internal sealed class IpcFrameProvider : IFrameProvider
{
    private readonly IpcConnection _connection;
    private readonly SharedMemoryBuffer[] _videoBuffers;
    private readonly bool _isHdr;
    private int _bufferIndex;

    // 先行リクエスト: 次フレームのレンダリングを事前に要求
    private Task<IpcMessage>? _prefetchTask;
    private int _prefetchBufferIndex;

    public IpcFrameProvider(IpcConnection connection, SharedMemoryBuffer[] videoBuffers,
        long frameCount, Rational frameRate, bool isHdr)
    {
        _connection = connection;
        _videoBuffers = videoBuffers;
        _isHdr = isHdr;
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

        if (_prefetchTask != null)
        {
            // 先行リクエスト済み: その結果を使う
            response = await _prefetchTask;
            readBufferIndex = _prefetchBufferIndex;
            _prefetchTask = null;
        }
        else
        {
            // 初回フレーム: 通常リクエスト
            readBufferIndex = _bufferIndex;
            var request = IpcMessage.Create(_connection.NextId(), MessageType.RequestFrame,
                new RequestFrameMessage { FrameIndex = frame, IsHdr = _isHdr, BufferIndex = readBufferIndex });
            response = await _connection.SendAndReceiveAsync(request)
                       ?? throw new IOException("Connection closed while waiting for frame");
        }

        if (response.Error != null)
            throw new InvalidOperationException($"Frame render failed: {response.Error}");

        var frameInfo = response.GetPayload<ProvideFrameMessage>()!;

        // 次フレームを先行リクエスト (ダブルバッファリング)
        long nextFrame = frame + 1;
        if (nextFrame < FrameCount)
        {
            _prefetchBufferIndex = 1 - readBufferIndex;
            var nextRequest = IpcMessage.Create(_connection.NextId(), MessageType.RequestFrame,
                new RequestFrameMessage { FrameIndex = nextFrame, IsHdr = _isHdr, BufferIndex = _prefetchBufferIndex });
            _prefetchTask = _connection.SendAndReceiveAsync(nextRequest).AsTask();
        }

        // 共有メモリからBitmap構築 (readBufferIndex側のバッファから読む)
        var colorType = _isHdr ? BitmapColorType.Rgba16161616 : BitmapColorType.Bgra8888;
        var alphaType = frameInfo.Premul ? BitmapAlphaType.Premul : BitmapAlphaType.Unpremul;
        var bmp = new Bitmap(frameInfo.Width, frameInfo.Height, colorType, alphaType, BitmapColorSpace.LinearSrgb);

        unsafe
        {
            _videoBuffers[readBufferIndex].Read(new Span<byte>((void*)bmp.Data, frameInfo.DataLength));
        }

        _bufferIndex = 1 - readBufferIndex;
        FramesRendered++;
        return bmp;
    }
}
