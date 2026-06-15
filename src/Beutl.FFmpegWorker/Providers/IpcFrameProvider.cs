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
    private int _bufferIndex;

    // 先行リクエスト: 次フレームのレンダリングを事前に要求
    private Task<IpcMessage>? _prefetchTask;
    private int _prefetchBufferIndex;
    private long _prefetchFrameIndex;

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
            // 先行リクエスト済みかつ要求フレームと一致: その結果を使う
            response = await _prefetchTask;
            readBufferIndex = _prefetchBufferIndex;
            _prefetchTask = null;
        }
        else
        {
            // プリフェッチが進行中だが要求フレームと一致しない場合 (シーク等の非連続要求) は、
            // 完了を待ってから結果を破棄する。参照を捨てるだけでなく必ず await することで、
            // 先行リクエストの応答をこのフレームの応答として取り違えたり、ホストへ非順次の
            // リクエストが流出するのを防ぐ。キャンセルや通信エラーはそのまま上位へ伝播させる。
            if (_prefetchTask != null)
            {
                Task<IpcMessage> staleTask = _prefetchTask;
                _prefetchTask = null;
                await staleTask;
            }

            // 初回フレーム or 非連続要求: 要求フレームを改めて取得
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

        // 次フレームを先行リクエスト (ダブルバッファリング)
        long nextFrame = frame + 1;
        if (nextFrame < FrameCount)
        {
            _prefetchBufferIndex = 1 - readBufferIndex;
            _prefetchFrameIndex = nextFrame;
            var nextRequest = IpcMessage.Create(_connection.NextId(), MessageType.RequestFrame,
                new RequestFrameMessage { FrameIndex = nextFrame, BufferIndex = _prefetchBufferIndex });
            _prefetchTask = _connection.SendAndReceiveAsync(nextRequest).AsTask();
        }

        // 共有メモリからBitmap構築 (readBufferIndex側のバッファから読む)
        var alphaType = frameInfo.Premul ? BitmapAlphaType.Premul : BitmapAlphaType.Unpremul;
        var bmp = new Bitmap(frameInfo.Width, frameInfo.Height, BitmapColorType.RgbaF16, alphaType, BitmapColorSpace.LinearSrgb);

        unsafe
        {
            _videoBuffers[readBufferIndex].Read(new Span<byte>((void*)bmp.Data, frameInfo.DataLength));
        }

        _bufferIndex = 1 - readBufferIndex;
        FramesRendered++;
        return bmp;
    }
}
