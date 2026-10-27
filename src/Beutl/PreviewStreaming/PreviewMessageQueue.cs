using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Beutl.PreviewStreaming;

// Playback state is replaceable; signaling, acknowledgements and revocations are not.
internal sealed class PreviewMessageQueue
{
    private readonly Channel<string?> _pending = Channel.CreateUnbounded<string?>();
    private readonly Dictionary<string, string> _playback = [];

    internal void Enqueue(string message, string? playbackId = null)
    {
        if (playbackId is null) { _pending.Writer.TryWrite(message); return; }
        lock (_playback)
        {
            if (_playback.Count == 0) _pending.Writer.TryWrite(null);
            _playback[playbackId] = message;
        }
    }

    internal async IAsyncEnumerable<string> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (string? message in _pending.Reader.ReadAllAsync(cancellationToken))
        {
            if (message is not null) { yield return message; continue; }
            string[] latest;
            lock (_playback)
            {
                latest = _playback.Values.ToArray();
                _playback.Clear();
            }
            foreach (string state in latest) yield return state;
        }
    }
}
