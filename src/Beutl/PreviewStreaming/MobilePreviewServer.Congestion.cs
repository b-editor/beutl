using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.PreviewStreaming;

internal sealed partial class MobilePreviewServer
{
    // Only direct viewers, the ones a narrow path can push off the direct settings.
    private readonly Dictionary<string, CongestionBackoff> _congestion = [];

    private void OnConnected(string id)
    {
        lock (_gate)
        {
            if (_congestion.TryGetValue(id, out var backoff)) backoff.Connected(_mediaClock.Elapsed);
            else if (_peers.TryGetValue(id, out var viewer) && !viewer.Relayed)
                (_congestion[id] = new CongestionBackoff()).Connected(_mediaClock.Elapsed);
        }
    }

    private void OnKeyFrameRequested(string id)
    {
        TimeSpan? hold;
        lock (_gate)
        {
            if (!_peers.TryGetValue(id, out var viewer) || viewer.Relayed) return;
            if (!_congestion.TryGetValue(id, out var backoff)) _congestion[id] = backoff = new CongestionBackoff();
            hold = backoff.KeyFrameRequested(_mediaClock.Elapsed);
        }
        if (hold is { } duration)
            Log.CreateLogger<MobilePreviewServer>().LogInformation(
                "A mobile preview viewer keeps losing video packets; using the relayed settings for {Hold}.", duration);
    }

    // A viewer that keeps asking for key frames is losing packets faster than NACKs repair them, the only sign of
    // a narrow path that the native transport passes on. The stream then uses the relayed settings for a while,
    // twice as long each time the same viewer loses packets again soon after.
    internal sealed class CongestionBackoff
    {
        internal const int Requests = 3;
        internal static readonly TimeSpan Window = TimeSpan.FromSeconds(10);
        // A browser asks for a key frame as it starts decoding, and again when its network path changes.
        internal static readonly TimeSpan Settling = TimeSpan.FromSeconds(5);
        internal static readonly TimeSpan FirstHold = TimeSpan.FromSeconds(30);
        internal static readonly TimeSpan LongestHold = TimeSpan.FromMinutes(5);
        private readonly Queue<TimeSpan> _requests = new();
        private TimeSpan _settled = TimeSpan.MinValue;
        private TimeSpan _hold;
        private TimeSpan? _until;

        internal void Connected(TimeSpan now)
        {
            _settled = now + Settling;
            _requests.Clear();
        }

        // Returns how long the relayed settings apply when this request starts a hold.
        internal TimeSpan? KeyFrameRequested(TimeSpan now)
        {
            if (now < _settled || IsCongested(now)) return null;
            _requests.Enqueue(now);
            while (now - _requests.Peek() > Window) _requests.Dequeue();
            if (_requests.Count < Requests) return null;
            _requests.Clear();
            _hold = _until is { } last && now - last < LongestHold
                ? (_hold * 2 < LongestHold ? _hold * 2 : LongestHold)
                : FirstHold;
            _until = now + _hold;
            return _hold;
        }

        internal bool IsCongested(TimeSpan now) => now < _until;
    }
}
