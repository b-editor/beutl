using Beutl.Models;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public partial class PlayerViewModel
{
    internal static string? FormatPlaybackRate(float speed, PlaybackDirection direction)
    {
        if (direction == ViewModels.PlaybackDirection.Stopped
            || (direction == ViewModels.PlaybackDirection.Forward && speed == 1f))
        {
            return null;
        }

        string sign = direction == ViewModels.PlaybackDirection.Backward ? "−" : "";
        return $"{sign}{speed.ToString("0.##", CultureInfo.InvariantCulture)}×";
    }

    public void ShuttleStop()
    {
        _isShuttling = false;
        if (IsPlaying.Value)
        {
            _ = Pause();
        }
        else
        {
            PlaybackSpeed.Value = 1.0f;
            PlaybackDirection.Value = ViewModels.PlaybackDirection.Stopped;
        }
    }

    public void ShuttleForward(bool fineGrain = false)
    {
        _ = ShuttleAsync(forward: true, fineGrain);
    }

    public void ShuttleBackward(bool fineGrain = false)
    {
        _ = ShuttleAsync(forward: false, fineGrain);
    }

    private async Task ShuttleAsync(bool forward, bool fineGrain)
    {
        try
        {
            await ShuttleCore(forward, fineGrain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred while starting shuttle playback.");
        }
    }

    private async Task ShuttleCore(bool forward, bool fineGrain)
    {
        if (_isDisposing || _isPausing || !_isEnabled.Value || Scene == null) return;
        var newDirection = forward
            ? ViewModels.PlaybackDirection.Forward
            : ViewModels.PlaybackDirection.Backward;

        // 通常再生中(L 押下時)はネイティブの再生を加速モードに切り替えるためまず停止
        if (IsPlaying.Value && !_isShuttling)
        {
            await Pause();

            // await 中に別の ShuttleCore が先行して StartShuttle を完了している可能性がある。
            // そのまま続行すると後続の PlaybackSpeed/Direction 設定で先行分を上書きしてしまうため、
            // 状態が変化していたらこの呼び出しは何もせずに抜ける。
            if (_isShuttling || IsPlaying.Value)
            {
                return;
            }
        }

        if (PlaybackDirection.Value != newDirection)
        {
            PlaybackSpeed.Value = fineGrain ? s_slowSpeeds[1] : 1.0f;
            PlaybackDirection.Value = newDirection;
        }
        else
        {
            // 同じ方向で連打されたら速度を倍々
            float current = PlaybackSpeed.Value;
            if (fineGrain)
            {
                int idx = Array.FindIndex(s_slowSpeeds, v => Math.Abs(v - current) < 0.001f);
                int nextIdx = idx < 0 ? 1 : Math.Min(idx + 1, s_slowSpeeds.Length - 1);
                PlaybackSpeed.Value = s_slowSpeeds[nextIdx];
            }
            else
            {
                int idx = Array.FindIndex(s_fastSpeeds, v => Math.Abs(v - current) < 0.001f);
                int nextIdx = idx < 0 ? 1 : Math.Min(idx + 1, s_fastSpeeds.Length - 1);
                PlaybackSpeed.Value = s_fastSpeeds[nextIdx];
            }
        }

        if (!_isShuttling)
        {
            StartShuttle();
        }
    }

    private void StartShuttle()
    {
        Scene? scene = Scene;
        if (_isDisposing || _isPausing || _isShuttling || scene == null) return;
        Rational rate = GetFrameRate();
        UsageTelemetry.Current?.Record("playback.started", feature: "shuttle");
        // Clear a stop request left by a prior Pause() so the flag's "true until the next
        // playback start" invariant holds across shuttle too, not just Play().
        int generation = _sessionGuard.Claim(() =>
        {
            _stopRequested = false;
            _isShuttling = true;
            IsPlaying.Value = true;
        });

        _playbackTask = Task.Run(() => RunShuttlePlaybackAsync(scene, rate, generation));
    }

    private async Task RunShuttlePlaybackAsync(Scene scene, Rational rate, int generation)
    {
        TimeSpan tick = TimeSpan.FromSeconds(1d / rate.ToDouble());
        MediaTime frameDuration = MediaTime.FromFrames(1, rate);
        PlaybackRange? selectedPass = null;
        bool firstTick = true;
        MediaTime position = default;
        MediaTime lastPublished = default;
        DateTime lastTime = DateTime.UtcNow;

        try
        {
            if (!_sessionGuard.TryApply(generation, () => scene.Edited -= OnSceneEdited))
                return;

            // Keep the render subscription and apply each tick's clock update on the UI thread.
            while (_isShuttling && IsPlaying.Value && _sessionGuard.Owns(generation))
            {
                bool advancing = false;
                if (!await TryApplyOnUIThreadAsync(generation, () => advancing = Advance()) || !advancing)
                    break;

                await Task.Delay(tick).ConfigureAwait(false);
            }
        }
        finally
        {
            // Only restore shared state while this shuttle still owns the session; a Pause()
            // timeout that disowned it must not let this late finally stomp a newer session.
            _sessionGuard.TryApply(generation, () =>
            {
                _isShuttling = false;
                IsPlaying.Value = false;
                PlaybackDirection.Value = ViewModels.PlaybackDirection.Stopped;
                PlaybackSpeed.Value = 1.0f;
                ReattachSceneEdited();
            });
        }

        bool Advance()
        {
            if (_isPausing || !_isShuttling || !IsPlaying.Value || Scene == null)
                return false;

            var direction = PlaybackDirection.Value;
            if (direction == ViewModels.PlaybackDirection.Stopped)
                return false;

            bool forward = direction == ViewModels.PlaybackDirection.Forward;
            bool looping = IsLoopEnabled.Value;
            if (!looping) selectedPass = null;
            else if (firstTick) selectedPass = GetSelectedLoopRange(scene, rate);
            bool firstStep = firstTick;
            firstTick = false;

            var range = GetBounds();
            DateTime now = DateTime.UtcNow;
            TimeSpan elapsed = now - lastTime;
            lastTime = now;

            MediaTime playhead = _editorClock.CurrentTime.Value;
            MediaTime currentTime = !firstStep && playhead == lastPublished ? position : playhead;
            MediaTime next = currentTime + MediaTime.FromSeconds(elapsed.TotalSeconds * PlaybackSpeed.Value * (forward ? 1 : -1));
            bool insideRange = currentTime >= range.Start && currentTime < range.End;
            if (!insideRange)
            {
                // Enter a selected range at the end the shuttle moves from, as normal playback enters at its start.
                if (selectedPass.HasValue) next = forward ? range.Start : range.Last;
                else if (currentTime >= range.End ? forward : !forward) return false;
                else if (next < MediaTime.Zero)
                {
                    if (currentTime <= MediaTime.Zero) return false;
                    next = MediaTime.Zero;
                }
            }
            else if (next >= range.End || next < range.Start)
            {
                if (forward ? currentTime < range.Last : currentTime > range.Start)
                {
                    // Show the boundary frame if the elapsed step jumped over it; without loop, stop there next tick.
                    next = forward ? range.Last : range.Start;
                }
                else if (!looping)
                {
                    return false;
                }
                else
                {
                    selectedPass = GetSelectedLoopRange(scene, rate);
                    range = GetBounds();
                    next = forward ? range.Start : range.Last;
                }
            }

            // At slow speeds the clock must keep advancing through the last frame, even while the playhead stays on it.
            position = next;
            lastPublished = insideRange && next > range.Last ? range.Last : next;
            _editorClock.Seek(lastPublished);
            return true;
        }

        (MediaTime Start, MediaTime End, MediaTime Last) GetBounds()
        {
            if (selectedPass is { } selection)
            {
                return (MediaTime.FromFrames(selection.StartFrame, rate), MediaTime.FromFrames(selection.EndFrame, rate),
                    MediaTime.FromFrames(selection.EndFrame - 1, rate));
            }

            MediaTime start = scene.Start.Value;
            MediaTime end = start + scene.Duration.Value;
            return (start, end, end - frameDuration);
        }
    }
}
