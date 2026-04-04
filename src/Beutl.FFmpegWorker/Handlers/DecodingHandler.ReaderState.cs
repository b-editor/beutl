using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegWorker.Decoding;
using Beutl.Media;

namespace Beutl.FFmpegWorker.Handlers;

internal sealed partial class DecodingHandler
{
    private sealed class ReaderState : IDisposable
    {
        public required FFmpegReader Reader { get; init; }

        // レガシーモード用ビデオバッファ (RingBuffer == null の場合に使用)
        public SharedMemoryBuffer? VideoBuffer { get; set; }
        public SharedMemoryBuffer? AudioBuffer { get; set; }

        // 色空間キャッシュ (レガシーモード用)
        public BitmapColorSpace? LastColorSpace;
        public float[]? LastTransferFn;
        public float[]? LastToXyzD50;

        // リングバッファモード (null の場合はレガシーモード)
        public VideoRingBuffer? RingBuffer { get; set; }

        public SemaphoreSlim ReaderLock { get; } = new(1, 1);

        public void Dispose()
        {
            RingBuffer?.Dispose();
            ReaderLock.Dispose();
            Reader.Dispose();
            VideoBuffer?.Dispose();
            AudioBuffer?.Dispose();
        }
    }
}
