using Beutl.Services;

namespace Beutl.ViewModels;

public partial class PlayerViewModel
{
    private readonly object _playbackActivitySync = new();
    private IDisposable? _playbackActivity;

    // Keeps the session of the scene's project busy from the moment playback starts until it has stopped and its
    // playback task has finished, so a loop re-arm or a pause still draining keeps it busy too.
    private void TrackPlaybackActivity()
    {
        SessionActivityTracker activity = _editViewModel.Workspace.Activity;
        IsPlaying.Subscribe(playing =>
            {
                if (playing)
                {
                    lock (_playbackActivitySync)
                    {
                        if (!_isDisposing)
                            _playbackActivity ??= activity.Begin(SessionActivityKind.Playback);
                    }
                }
                else
                {
                    // A pause ends the activity once it has drained; a natural end does when its task finishes.
                    _ = Volatile.Read(ref _playbackTask).ContinueWith(
                        _ => EndPlaybackActivityIfStopped(),
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            })
            .DisposeWith(_disposables);
    }

    private void EndPlaybackActivityIfStopped()
    {
        IDisposable? activity;
        lock (_playbackActivitySync)
        {
            if (IsPlaybackActive)
                return;

            activity = _playbackActivity;
            _playbackActivity = null;
        }

        activity?.Dispose();
    }

    // Disposal ends the activity whatever playback was doing.
    private void EndPlaybackActivity()
    {
        IDisposable? activity;
        lock (_playbackActivitySync)
        {
            activity = _playbackActivity;
            _playbackActivity = null;
        }

        activity?.Dispose();
    }
}
