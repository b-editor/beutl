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
    private readonly SharedMemoryBuffer _videoBuffer;
    private readonly bool _isHdr;

    public IpcFrameProvider(IpcConnection connection, SharedMemoryBuffer videoBuffer,
        long frameCount, Rational frameRate, bool isHdr)
    {
        _connection = connection;
        _videoBuffer = videoBuffer;
        _isHdr = isHdr;
        FrameCount = frameCount;
        FrameRate = frameRate;
    }

    public long FrameCount { get; }
    public Rational FrameRate { get; }
    public long FramesRendered { get; private set; }

    public async ValueTask<Bitmap> RenderFrame(long frame)
    {
        // Main側にフレーム要求
        var request = IpcMessage.Create(_connection.NextId(), MessageType.RequestFrame,
            new RequestFrameMessage { FrameIndex = frame, IsHdr = _isHdr });
        var response = await _connection.SendAndReceiveAsync(request)
                       ?? throw new IOException("Connection closed while waiting for frame");

        if (response.Error != null)
            throw new InvalidOperationException($"Frame render failed: {response.Error}");

        var frameInfo = response.GetPayload<ProvideFrameMessage>()!;

        // 共有メモリからBitmap構築
        var colorType = _isHdr ? BitmapColorType.Rgba16161616 : BitmapColorType.Bgra8888;
        var alphaType = frameInfo.Premul ? BitmapAlphaType.Premul : BitmapAlphaType.Unpremul;
        var bmp = new Bitmap(frameInfo.Width, frameInfo.Height, colorType, alphaType, BitmapColorSpace.LinearSrgb);

        unsafe
        {
            _videoBuffer.Read(new Span<byte>((void*)bmp.Data, frameInfo.DataLength));
        }

        FramesRendered++;
        return bmp;
    }
}
