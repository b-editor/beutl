namespace Beutl.ViewModels;

internal sealed class AudioPlaybackClock
{
    private readonly object _lock = new();
    private TimeSpan _anchorAudioTime;
    private long _anchorTimestamp;
    private bool _running;

    public void Anchor(TimeSpan audioTime)
    {
        lock (_lock)
        {
            _anchorAudioTime = audioTime;
            _anchorTimestamp = Stopwatch.GetTimestamp();
            _running = true;
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            _running = false;
        }
    }

    public TimeSpan? GetTime()
    {
        lock (_lock)
        {
            if (!_running) return null;
            return _anchorAudioTime + Stopwatch.GetElapsedTime(_anchorTimestamp);
        }
    }
}
