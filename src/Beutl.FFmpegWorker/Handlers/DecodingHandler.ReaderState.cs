using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegWorker.Decoding;

namespace Beutl.FFmpegWorker.Handlers;

internal sealed partial class DecodingHandler
{
    private sealed class ReaderState : IDisposable
    {
        public required FFmpegReader Reader { get; init; }

        public SharedMemoryBuffer? AudioBuffer { get; set; }

        // リングバッファ (動画リーダーのときに生成される)
        public VideoRingBuffer? RingBuffer { get; set; }

        public SemaphoreSlim ReaderLock { get; } = new(1, 1);

        public void Dispose()
        {
            RingBuffer?.Dispose();
            ReaderLock.Dispose();
            Reader.Dispose();
            AudioBuffer?.Dispose();
        }
    }
}
