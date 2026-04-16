namespace Beutl.ViewModels;

internal sealed class AudioPlaybackClock
{
    private readonly object _lock = new();
    private readonly TaskCompletionSource _started =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TimeSpan _anchorAudioTime;
    private long _anchorTimestamp;
    private bool _running;

    // 音声再生の準備が完了した（または失敗して終了した）ことを通知するタスク。
    // これを待つことで、映像側のウォールクロック基準点を音声開始と揃えられる。
    public Task StartedTask => _started.Task;

    public void SignalStarted() => _started.TrySetResult();

    public void Anchor(TimeSpan audioTime)
    {
        lock (_lock)
        {
            _anchorAudioTime = audioTime;
            _anchorTimestamp = Stopwatch.GetTimestamp();
            _running = true;
        }
        _started.TrySetResult();
    }

    public void Pause()
    {
        lock (_lock)
        {
            _running = false;
        }
        _started.TrySetResult();
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
