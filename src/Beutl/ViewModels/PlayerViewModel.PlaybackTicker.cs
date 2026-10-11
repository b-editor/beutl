using Avalonia.Threading;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Models;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels;

public partial class PlayerViewModel
{
    // Drives one playback run from the frame timer: each tick shows the frames that are due, and the run
    // completes when playback stops, the renderer fails or the end of the scene is reached.
    // The timer fires on a thread-pool thread, but the state the editor follows (the shown frame, the clock and
    // IsPlaying) changes on the UI thread, as in the shuttle loop: subscribers such as the graph editor and the
    // property editors read collections that the UI thread edits. Ticks post those changes and never wait for them.
    private sealed class PlaybackTicker
    {
        private readonly PlayerViewModel _owner;
        private readonly int _generation;
        private readonly BufferedPlayer _playerImpl;
        private readonly AudioPlaybackClock _clock;
        private readonly int _rate;
        private readonly TimeSpan _tick;
        private readonly int _startFrame;
        private readonly int _endFrame;
        private readonly TimeSpan _startTime;
        private readonly DateTime _startDateTime;
        // Completed from the timer and the UI thread; PlayInternal must not continue inline on either.
        private readonly TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _nextExpectedFrame;
        private int _processing;
        private bool _renderFailureHandled;
        // The newest frame waiting for the UI thread, and whether a post that shows it is queued.
        private PendingFrame? _pendingFrame;
        private int _framePostQueued;
        // Set on the UI thread once the run has ended; whatever the run posts after that changes nothing.
        private bool _retired;

        public PlaybackTicker(
            PlayerViewModel owner,
            int generation,
            BufferedPlayer playerImpl,
            AudioPlaybackClock clock,
            int rate,
            TimeSpan tick,
            int startFrame,
            int endFrame,
            TimeSpan startTime,
            DateTime startDateTime)
        {
            _owner = owner;
            _generation = generation;
            _playerImpl = playerImpl;
            _clock = clock;
            _rate = rate;
            _tick = tick;
            _startFrame = startFrame;
            _endFrame = endFrame;
            _startTime = startTime;
            _startDateTime = startDateTime;
            _nextExpectedFrame = startFrame + 1;
        }

        public Task Completion => _tcs.Task;

        // Set when the run stopped at the end of the scene with looping enabled.
        public bool ReachedNaturalEnd { get; private set; }

        // Called on the UI thread after the run completed, behind everything it posted, so that nothing a tick still
        // in progress posts can land after the stopped state or the loop's rewind.
        public void Retire()
        {
            Dispatcher.UIThread.VerifyAccess();
            _retired = true;
        }

        private int ComputeExpectFrame()
        {
            TimeSpan elapsed = _clock.GetTime() is { } audioTime
                ? audioTime - _startTime
                : DateTime.UtcNow - _startDateTime;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            return (int)(elapsed.Ticks / _tick.Ticks) + _startFrame;
        }

        private bool StopForRenderFailure()
        {
            BufferedPlayer.RenderFailure? failure = _playerImpl.Failure;
            if (failure is null)
                return false;

            if (_renderFailureHandled)
                return true;

            _renderFailureHandled = true;
            Post(() => _owner.ApplyPlaybackRenderFailure(
                failure,
                _rate,
                () => _owner._sessionGuard.Owns(_generation)));
            _tcs.TrySetResult(true);
            return true;
        }

        public void OnTick(object? state)
        {
            if (Interlocked.Exchange(ref _processing, 1) != 0) return;
            try
            {
                // A completed run posts nothing more, so Retire comes after all it posted.
                if (_tcs.Task.IsCompleted)
                    return;

                // Do not let a timer callback touch state after disposal.
                if (_owner._isDisposing)
                {
                    _tcs.TrySetResult(true);
                    return;
                }

                // A Pause() timeout may have disowned this task while it is still blocked in
                // PlayInternal's WhenAll; once a newer session has claimed, stop the loop instead of
                // dequeuing frames or writing shared preview state (PreviewImage / CurrentTime /
                // IsPlaying) over the session that replaced it. The generation guard on PlayInternal's
                // finally alone runs too late — the timer keeps firing until PlayInternal returns.
                if (!_owner._sessionGuard.Owns(_generation))
                {
                    _tcs.TrySetResult(true);
                    return;
                }

                // The producer publishes its terminal render exception before ProducerStopped.
                // Stop before consuming any older frames still buffered ahead of the playhead,
                // otherwise a pre-failure frame could hide the error that ended playback.
                if (StopForRenderFailure())
                {
                    return;
                }

                var expectFrame = ComputeExpectFrame();
                if (_owner._stopRequested || !_owner.IsPlaying.Value || expectFrame >= _endFrame)
                {
                    bool clearIsPlaying = false;
                    _owner._sessionGuard.TryApply(_generation, () =>
                    {
                        // ループ用に endFrame で打ち切る場合、音声側にも停止を伝える。
                        // ただし停止要求中はループの自然終端とみなさず、再開させない。
                        if (!_owner._stopRequested && _owner.IsLoopEnabled.Value && expectFrame >= _endFrame)
                        {
                            ReachedNaturalEnd = true;
                            clearIsPlaying = true;
                        }
                        else if (_owner._stopRequested)
                        {
                            // A pause that raced the loop re-arm leaves IsPlaying=true; clear it so the
                            // audio task stops here instead of running to the scene's natural end.
                            clearIsPlaying = true;
                        }
                    });

                    if (clearIsPlaying)
                        Post(() => _owner.IsPlaying.Value = false);

                    _tcs.TrySetResult(true);
                    return;
                }

                if (expectFrame < _nextExpectedFrame)
                {
                    return;
                }

                bool dequeued = false;
                while (_playerImpl.TryDequeue(out IPlayer.Frame frame))
                {
                    dequeued = true;
                    using (frame.Bitmap)
                    {
                        if (!_owner._sessionGuard.Owns(_generation))
                        {
                            _tcs.TrySetResult(true);
                            return;
                        }

                        Show(frame.Bitmap.Clone(), frame.Time);
                    }

                    // タイマーが正確じゃないから、だんだんとフレームがずれてくる
                    // そのため、フレームを消費しすぎたら、そのフレーム番号とexpectFrameが一致するまでスキップする
                    // 逆に、フレームを消費しすぎない場合は、そのまま次のフレームを取得する
                    if (expectFrame <= frame.Time)
                    {
                        _nextExpectedFrame = frame.Time + 1;
                        break;
                    }

                    // 期待していたフレームよりも前のフレームが来た場合
                }

                // Close the race where the producer faults while this timer callback is
                // dequeuing the last successfully rendered frame.
                if (StopForRenderFailure())
                {
                    return;
                }

                if (!dequeued && _playerImpl.ProducerStopped)
                {
                    // ProducerStopped is published after Failure. Re-read Failure only after
                    // observing the terminal flag so a fault published in the preceding gap
                    // cannot be mistaken for a normal producer stop.
                    if (StopForRenderFailure())
                    {
                        return;
                    }

                    Post(() => _owner.IsPlaying.Value = false);
                    _tcs.TrySetResult(true);
                    return;
                }

                _playerImpl.Skipped(ComputeExpectFrame() + 1);
            }
            catch (Exception ex)
            {
                // An exception that escapes a timer callback ends the process.
                Stop(ex);
            }
            finally
            {
                Interlocked.Exchange(ref _processing, 0);
            }
        }

        // A frame that the UI thread has not shown yet is replaced by a newer one, so a busy UI thread shows the
        // newest frame next instead of falling behind the clock while it holds every frame posted meanwhile.
        private void Show(Ref<Bitmap> preview, int time)
        {
            Interlocked.Exchange(ref _pendingFrame, new PendingFrame(preview, time))?.Preview.Dispose();
            if (Interlocked.Exchange(ref _framePostQueued, 1) == 0)
                Dispatcher.UIThread.Post(ShowPendingFrame, DispatcherPriority.Normal);
        }

        private void ShowPendingFrame()
        {
            // Cleared before taking the frame, so a frame stored after the take queues another post.
            Volatile.Write(ref _framePostQueued, 0);
            if (Interlocked.Exchange(ref _pendingFrame, null) is not { } frame)
                return;

            bool shown = false;
            try
            {
                if (_retired)
                    return;

                _owner._sessionGuard.TryApply(_generation, () =>
                {
                    // PreviewImage owns the frame from here on.
                    shown = true;
                    _owner.UpdateImage(frame.Preview);
                    _owner.ClearPreviewRenderError(_generation);

                    if (_owner.Scene != null)
                    {
                        _owner._editorClock.CurrentTime.Value = frame.Time.ToTimeSpan(_rate);
                        _owner.EditViewModel.FrameCacheManager.Value.CurrentFrame = frame.Time;
                    }
                });
            }
            catch (Exception ex)
            {
                // An exception that escapes a UI dispatcher job ends the process too.
                Stop(ex);
            }
            finally
            {
                if (!shown)
                    frame.Preview.Dispose();
            }
        }

        // Applies a change of the session's state on the UI thread, after the frames posted before it.
        private void Post(Action update)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_retired)
                    return;

                try
                {
                    _owner._sessionGuard.TryApply(_generation, update);
                }
                catch (Exception ex)
                {
                    Stop(ex);
                }
            }, DispatcherPriority.Normal);
        }

        // Ends playback after a failure in a tick or in a subscriber of the state it changes, instead of the process.
        private void Stop(Exception ex)
        {
            _owner._logger.LogError(ex, "An exception occurred during playback; stopping the playback. ({SceneId})",
                _owner._editViewModel.SceneId);
            // Posted before completing, so that it comes before Retire.
            Dispatcher.UIThread.Post(() =>
            {
                if (_retired)
                    return;

                try
                {
                    _owner._sessionGuard.TryApply(_generation, () => _owner.IsPlaying.Value = false);
                }
                catch (Exception stopEx)
                {
                    _owner._logger.LogError(stopEx, "An exception occurred while stopping the playback. ({SceneId})",
                        _owner._editViewModel.SceneId);
                }
            }, DispatcherPriority.Normal);
            _tcs.TrySetResult(true);
        }

        private sealed record PendingFrame(Ref<Bitmap> Preview, int Time);
    }
}
