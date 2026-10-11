using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegWorker.Decoding;

namespace Beutl.FFmpegWorker.Handlers;

internal sealed partial class DecodingHandler
{
    private sealed class ReaderState : IDisposable
    {
        // Operations in progress on the reader, and whether a close has begun. Never disposed, so an operation that
        // looked the reader up just before its close can still check it.
        private readonly object _useGate = new();
        private int _uses;
        private bool _closing;

        public required FFmpegReader Reader { get; init; }

        public SharedMemoryBuffer? AudioBuffer { get; set; }

        // Ring buffer; created when the reader has a video stream.
        public VideoRingBuffer? RingBuffer { get; set; }

        public SemaphoreSlim ReaderLock { get; } = new(1, 1);

        // False once a close has begun.
        public bool TryBeginUse()
        {
            lock (_useGate)
            {
                if (_closing)
                    return false;

                _uses++;
                return true;
            }
        }

        public void EndUse()
        {
            lock (_useGate)
            {
                if (--_uses == 0 && _closing)
                    Monitor.PulseAll(_useGate);
            }
        }

        // Refuses new uses and waits for the ones in progress to end.
        public void StopUses()
        {
            lock (_useGate)
            {
                _closing = true;
                while (_uses > 0)
                    Monitor.Wait(_useGate);
            }
        }

        public void Dispose()
        {
            RingBuffer?.Dispose();
            ReaderLock.Dispose();
            Reader.Dispose();
            AudioBuffer?.Dispose();
        }
    }

    // An operation in progress on a reader, which the reader's close waits for. State is null when the reader is
    // unknown or a close has begun.
    private readonly struct ReaderUse(ReaderState? state) : IDisposable
    {
        public ReaderState? State { get; } = state;

        public void Dispose() => State?.EndUse();
    }
}
