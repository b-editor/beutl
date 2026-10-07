using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public partial class PlayerViewModel
{
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
        int rate = GetFrameRate();
        UsageTelemetry.Current?.Record("playback.started", feature: "shuttle");
        // Clear a stop request left by a prior Pause() so the flag's "true until the next
        // playback start" invariant holds across shuttle too, not just Play().
        int generation = _sessionGuard.Claim(() =>
        {
            _stopRequested = false;
            _isShuttling = true;
            IsPlaying.Value = true;
        });

        _playbackTask = Task.Run(async () =>
        {
            try
            {
                TimeSpan tick = TimeSpan.FromSeconds(1d / rate);

                if (!_sessionGuard.TryApply(generation, () => scene.Edited -= OnSceneEdited))
                {
                    return;
                }
                // 既存の CurrentFrame 購読は維持して UpdateCurrentFrame 経由でレンダリングを行う

                DateTime lastTime = DateTime.UtcNow;
                while (_isShuttling
                       && IsPlaying.Value
                       && Scene != null
                       && _sessionGuard.Owns(generation))
                {
                    var direction = PlaybackDirection.Value;
                    if (direction == ViewModels.PlaybackDirection.Stopped)
                        break;

                    // シャトル再生中に Scene.Start / Duration が変更されても追従できるよう毎回取得
                    TimeSpan sceneStart = default, sceneEnd = default;
                    if (!_sessionGuard.TryApply(generation, () =>
                        {
                            sceneStart = scene.Start;
                            sceneEnd = sceneStart + scene.Duration;
                        }))
                        break;

                    DateTime now = DateTime.UtcNow;
                    TimeSpan elapsed = now - lastTime;
                    lastTime = now;

                    float speed = PlaybackSpeed.Value;
                    int sign = direction == ViewModels.PlaybackDirection.Forward ? 1 : -1;
                    TimeSpan delta = TimeSpan.FromTicks((long)(elapsed.Ticks * speed * sign));
                    TimeSpan currentTime = _editorClock.CurrentTime.Value;
                    TimeSpan next = currentTime + delta;

                    // 範囲外から再生開始した場合はその位置から自然に再生する。
                    // シーン範囲内に入った時点で通常のループ・境界判定に切り替わる。
                    bool insideRange = currentTime >= sceneStart && currentTime < sceneEnd;
                    if (insideRange)
                    {
                        TimeSpan minTime = sceneStart;
                        TimeSpan maxTime = sceneEnd - tick;
                        if (IsLoopEnabled.Value)
                        {
                            if (next > sceneEnd) next = minTime;
                            if (next < sceneStart) next = maxTime;
                        }
                        else
                        {
                            if (next >= sceneEnd) { next = maxTime; break; }
                            if (next < sceneStart) { next = minTime; break; }
                        }

                        if (next < minTime) next = minTime;
                        if (next > maxTime) next = maxTime;
                    }
                    else
                    {
                        // 範囲外: シーン範囲に向かう方向のみ進行を許可する。
                        bool movingTowardRange = currentTime >= sceneEnd ? sign < 0 : sign > 0;
                        if (!movingTowardRange) break;
                        if (next < TimeSpan.Zero) { next = TimeSpan.Zero; break; }
                    }

                    TimeSpan target = next;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _sessionGuard.TryApply(generation, () =>
                        {
                            if (Scene != null)
                            {
                                _editorClock.CurrentTime.Value = target;
                            }
                        });
                    });

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
        });
    }
}
