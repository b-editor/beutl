using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.PreviewStreaming;

internal sealed partial class MobilePreviewServer
{
    // Direct viewers that have connected, the only ones a narrow path can push off the direct settings.
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
        CongestionBackoff? backoff;
        TimeSpan now;
        TimeSpan? hold;
        lock (_gate)
        {
            // Requests count from the time a viewer connects. Video can reach the browser, and the browser ask for
            // a key frame to start decoding, before the connection reports connected, which waits for the data channel.
            if (!_peers.TryGetValue(id, out var viewer) || viewer.Relayed
                || !_congestion.TryGetValue(id, out backoff)) return;
            now = _mediaClock.Elapsed;
            hold = backoff.KeyFrameRequested(now);
        }
        if (hold is not { } duration) return;
        Log.CreateLogger<MobilePreviewServer>().LogInformation(
            "A mobile preview viewer keeps losing video packets; using the relayed settings for {Hold}.", duration);
        _ = SendAgainWhenHoldEndsAsync(id, backoff, now + duration);
    }

    // A paused frame is coded only when something changes, so the end of a hold has to send it again at the direct
    // settings, as the last relayed viewer leaving does. Going back to them starts with a key frame anyway.
    private async Task SendAgainWhenHoldEndsAsync(string id, CongestionBackoff backoff, TimeSpan end)
    {
        try
        {
            // Task.Delay can return a moment before the media clock reaches the end of the hold.
            for (var left = end - _mediaClock.Elapsed; left > TimeSpan.Zero; left = end - _mediaClock.Elapsed)
                await Task.Delay(left + TimeSpan.FromMilliseconds(1), _stop.Token).ConfigureAwait(false);
            lock (_gate)
            {
                // Not for a viewer that has left since, nor while another viewer still needs the relayed settings.
                if (_congestion.TryGetValue(id, out var current) && current == backoff && !UsesRelayedSettings())
                    RequestKeyFrame();
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    // A viewer that keeps asking for key frames is losing packets faster than NACKs repair them, the only sign of
    // a narrow path that the native transport passes on. The stream then uses the relayed settings for a while,
    // twice as long each time the same viewer loses packets again soon after. Only tests pass a shorter first hold.
    internal sealed class CongestionBackoff(TimeSpan firstHold)
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

        internal CongestionBackoff() : this(FirstHold) { }

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
                : firstHold;
            _until = now + _hold;
            return _hold;
        }

        internal bool IsCongested(TimeSpan now) => now < _until;
    }
}
