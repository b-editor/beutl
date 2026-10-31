using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.SharedMemory;

namespace Beutl.Extensions.FFmpeg;

public sealed class PreviewEncoder : IDisposable
{
    private readonly FFmpegWorkerProcess _worker = FFmpegWorkerProcess.CreateForEncoding();
    private readonly SharedMemoryBuffer _buffer;
    private readonly PreviewEncoderStart _settings;
    private bool _started;

    public PreviewEncoder(int width, int height, bool hdr, bool audio = false)
    {
        int capacity = PreviewEncodingFormat.BufferBytes(width, height, hdr, audio);
        _buffer = SharedMemoryBuffer.Create($"beutl-preview-{Guid.NewGuid():N}", capacity);
        _settings = new(_buffer.Name, capacity, width, height, hdr, audio);
    }

    // Serialized by the caller; the worker consumes the buffer before acknowledging each request.
    public async Task<byte[]> EncodeAsync(ReadOnlyMemory<byte> pixels, bool keyFrame, CancellationToken ct)
    {
        var connection = await _worker.EnsureStartedAsync(ct).ConfigureAwait(false);
        if (!_started)
        {
            var response = await connection.SendAndReceiveAsync(
                IpcMessage.Create(connection.NextId(), MessageType.StartPreviewEncoder, _settings), ct).ConfigureAwait(false);
            if (response.Type == MessageType.Error) throw new InvalidOperationException(response.Error);
            _started = true;
        }
        if (pixels.Length != _buffer.Capacity) throw new ArgumentException("Unexpected preview frame size.", nameof(pixels));
        _buffer.Write(pixels.Span);
        var packet = await connection.RequestAsync<PreviewEncoderFrame, PreviewEncodedPacket>(
            MessageType.EncodePreviewFrame, MessageType.EncodePreviewFrameResult, new(keyFrame), ct).ConfigureAwait(false);
        return packet.Data;
    }

    public void Dispose()
    {
        _worker.Dispose();
        _buffer.Dispose();
    }
}
