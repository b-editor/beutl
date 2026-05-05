using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Platforms.XAudio2;
using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics3D.Gizmo;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Silk.NET.OpenAL;
using SkiaSharp;
using Vortice.Multimedia;
using AudioContext = Beutl.Audio.Platforms.OpenAL.AudioContext;

namespace Beutl.ViewModels;

public enum PlaybackDirection
{
    Stopped,
    Forward,
    Backward,
}

public sealed class PlayerViewModel : IAsyncDisposable, IPreviewPlayer
{
    private static readonly TimeSpan s_second = TimeSpan.FromSeconds(1);
    private static readonly float[] s_fastSpeeds = [1.0f, 2.0f, 4.0f, 8.0f, 16.0f, 32.0f];
    private static readonly float[] s_slowSpeeds = [1.0f, 0.5f, 0.25f];
    private readonly ILogger _logger = Log.CreateLogger<PlayerViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly ReactivePropertySlim<bool> _isEnabled;
    private readonly EditViewModel _editViewModel;
    private readonly IEditorClock _editorClock;
    private readonly IEditorSelection _editorSelection;
    private IDisposable? _currentFrameSubscription;
    private CancellationTokenSource? _cts;
    private Size _maxFrameSize;
    private Task _playbackTask = Task.CompletedTask;
    private bool _isShuttling;
    // Published snapshots carry the start time of the buffer that was just *queued*
    // to the audio backend, which is ahead of the current playhead. Replay several
    // recent snapshots so a visualizer tab opened mid-playback also receives the
    // already-consumed buffers, avoiding a silent ring-buffer window until the
    // playhead catches up to the queued-but-not-yet-played audio.
    private readonly ReplaySubject<AudioFrameSnapshot> _audioFramePushed = new(bufferSize: 8);

    public PlayerViewModel(EditViewModel editViewModel)
    {
        _editViewModel = editViewModel;
        _editorClock = editViewModel.GetRequiredService<IEditorClock>();
        _editorSelection = editViewModel.GetRequiredService<IEditorSelection>();
        Scene = editViewModel.Scene;
        _isEnabled = editViewModel.IsEnabled;
        PlayPause = new AsyncReactiveCommand(_isEnabled.AsObservable())
            .WithSubscribe(async () =>
            {
                if (IsPlaying.Value)
                {
                    await Pause();
                }
                else
                {
                    Play();
                }
            })
            .DisposeWith(_disposables);

        Next = new ReactiveCommand(_isEnabled)
            .WithSubscribe(() =>
            {
                int rate = GetFrameRate();
                UpdateCurrentFrame(_editorClock.CurrentTime.Value + TimeSpan.FromSeconds(1d / rate));
            })
            .DisposeWith(_disposables);

        Previous = new ReactiveCommand(_isEnabled)
            .WithSubscribe(() =>
            {
                int rate = GetFrameRate();
                UpdateCurrentFrame(_editorClock.CurrentTime.Value - TimeSpan.FromSeconds(1d / rate));
            })
            .DisposeWith(_disposables);

        Start = new ReactiveCommand(_isEnabled)
            .WithSubscribe(() =>
            {
                int rate = GetFrameRate();
                var endTime = Scene.Start + Scene.Duration - TimeSpan.FromSeconds(1d / rate);
                // 現在の時間がスタートと同じ場合、0に移動
                _editorClock.CurrentTime.Value =
                    _editorClock.CurrentTime.Value > endTime
                        ? endTime
                        : _editorClock.CurrentTime.Value > Scene.Start
                            ? Scene.Start
                            : TimeSpan.Zero;
            })
            .DisposeWith(_disposables);

        End = new ReactiveCommand(_isEnabled)
            .WithSubscribe(() =>
            {
                int rate = GetFrameRate();
                var endTime = Scene.Start + Scene.Duration - TimeSpan.FromSeconds(1d / rate);
                _editorClock.CurrentTime.Value =
                    _editorClock.CurrentTime.Value < Scene.Start
                        ? Scene.Start
                        : _editorClock.CurrentTime.Value < endTime
                            ? endTime
                            : Scene.Children.Count > 0
                                ? Scene.Children.Max(i => i.Start + i.Length) - TimeSpan.FromSeconds(1d / rate)
                                : TimeSpan.Zero;
            })
            .DisposeWith(_disposables);

        Scene.Edited += OnSceneEdited;

        _isEnabled.Subscribe(async v =>
            {
                if (!v && IsPlaying.Value)
                {
                    await Pause();
                }
            })
            .DisposeWith(_disposables);

        CurrentFrame = _editorClock.CurrentTime
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        _currentFrameSubscription = CurrentFrame.Subscribe(UpdateCurrentFrame);

        Duration = _editorClock.MaximumTime
            .CombineLatest(Scene.GetObservable(Scene.DurationProperty), Scene.GetObservable(Scene.StartProperty),
                CurrentFrame)
            .Select(i =>
            {
                // このDurationはSliderの最大値に使うので、一フレーム分を引く
                var frame = TimeSpan.FromSeconds(1.0 / GetFrameRate());
                return TimeSpan.FromTicks(Math.Max(
                    Math.Max(i.First.Ticks - frame.Ticks, i.Second.Ticks + i.Third.Ticks - frame.Ticks),
                    i.Fourth.Ticks));
            })
            .ToReadOnlyReactiveProperty()
            .DisposeWith(_disposables);

        PathEditor = new PathEditorViewModel(_editViewModel, this)
            .DisposeWith(_disposables);

        // カメラモードが解除されたらGizmoを非表示にする
        IsCameraMode.Subscribe(isCameraMode =>
            {
                if (!isCameraMode)
                {
                    ClearAllGizmoTargets();
                }
            })
            .DisposeWith(_disposables);

        // GizmoModeが変更されたらScene3Dに反映する
        SelectedGizmoMode.Subscribe(mode =>
            {
                if (IsCameraMode.Value)
                {
                    UpdateAllGizmoModes(mode);
                }
            })
            .DisposeWith(_disposables);

        ToneMappingMode = GlobalConfiguration.Instance.EditorConfig.GetObservable(EditorConfig.ToneMappingModeProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        ToneMappingExposure = GlobalConfiguration.Instance.EditorConfig.GetObservable(EditorConfig.ToneMappingExposureProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
    }

    private void ClearAllGizmoTargets()
    {
        foreach (var element in Scene?.Children ?? [])
        {
            foreach (var obj in element.Objects)
            {
                if (obj is Graphics3D.Scene3D scene3D)
                {
                    scene3D.GizmoTarget.CurrentValue = null;
                    scene3D.GizmoMode.CurrentValue = GizmoMode.None;
                }
            }
        }
    }

    private void UpdateAllGizmoModes(GizmoMode mode)
    {
        if (Scene == null) return;

        foreach (var scene3D in Scene.Children
                     .SelectMany(e => e.Objects)
                     .OfType<Graphics3D.Scene3D>()
                     .Where(s => s.GizmoTarget.CurrentValue.HasValue))
        {
            // GizmoTargetが設定されている場合のみモードを更新
            scene3D.GizmoMode.CurrentValue = mode;
        }
    }

    private void OnSceneEdited(object? sender, EventArgs e)
    {
        if (e is ElementEditedEventArgs elementEdited)
        {
            TimeSpan time = _editorClock.CurrentTime.Value;
            if (!elementEdited.AffectedRange.Any(v => v.Contains(time)))
            {
                return;
            }
        }

        QueueRender();
    }

    public Subject<Unit> AfterRendered { get; } = new();

    public Scene? Scene { get; set; }

    public Project? Project => Scene?.FindHierarchicalParent<Project>();

    public ReactivePropertySlim<Ref<Bitmap>?> PreviewImage { get; } = new();

    IReadOnlyReactiveProperty<Ref<Bitmap>?> IPreviewPlayer.PreviewImage => PreviewImage;

    IObservable<Unit> IPreviewPlayer.AfterRendered => AfterRendered;

    IReadOnlyReactiveProperty<bool> IPreviewPlayer.IsPlaying => IsPlaying;

    IObservable<AudioFrameSnapshot> IPreviewPlayer.AudioFramePushed => _audioFramePushed;

    private void PublishAudioSnapshot(Pcm<Stereo32BitFloat>? pcm, TimeSpan startTime)
    {
        if (pcm == null) return;

        // Always publish so the ReplaySubject retains the latest snapshot — a
        // visualizer tab opened after this point can replay it on subscribe.
        int samples = pcm.NumSamples;
        int channels = pcm.NumChannels;
        var interleaved = new float[samples * channels];
        MemoryMarshal.Cast<Stereo32BitFloat, float>(pcm.DataSpan).CopyTo(interleaved);
        _audioFramePushed.OnNext(new AudioFrameSnapshot(interleaved, pcm.SampleRate, channels, startTime));
    }

    Task<AudioFrameSnapshot?> IPreviewPlayer.ComposeAudioAsync(TimeSpan start, TimeSpan duration, CancellationToken ct)
    {
        SceneComposer? composer = EditViewModel.Composer.Value;
        if (composer == null || composer.IsDisposed)
            return Task.FromResult<AudioFrameSnapshot?>(null);

        return ComposeThread.Dispatcher.InvokeAsync(() =>
        {
            ct.ThrowIfCancellationRequested();
            using AudioBuffer? audio = composer.Compose(new TimeRange(start, duration));
            if (audio == null) return (AudioFrameSnapshot?)null;

            int samples = audio.SampleCount;
            int channels = audio.ChannelCount;
            var interleaved = new float[samples * channels];
            for (int c = 0; c < channels; c++)
            {
                Span<float> src = audio.GetChannelData(c);
                for (int f = 0; f < samples; f++)
                {
                    interleaved[f * channels + c] = src[f];
                }
            }
            return (AudioFrameSnapshot?)new AudioFrameSnapshot(interleaved, audio.SampleRate, channels, start);
        }, ct: ct);
    }

    public ReactivePropertySlim<bool> IsPlaying { get; } = new();

    public ReactivePropertySlim<float> PlaybackSpeed { get; } = new(1.0f);

    public ReactivePropertySlim<PlaybackDirection> PlaybackDirection { get; } = new(ViewModels.PlaybackDirection.Stopped);

    public ReactivePropertySlim<bool> IsLoopEnabled { get; } = new(false);

    public ReactiveProperty<TimeSpan> CurrentFrame { get; }

    public ReadOnlyReactiveProperty<TimeSpan> Duration { get; }

    public AsyncReactiveCommand PlayPause { get; }

    public ReactiveCommand Next { get; }

    public ReactiveCommand Previous { get; }

    public ReactiveCommand Start { get; }

    public ReactiveCommand End { get; }

    public ReactivePropertySlim<bool> IsMoveMode { get; } = new(true);

    public ReactivePropertySlim<bool> IsHandMode { get; } = new(false);

    public ReactivePropertySlim<bool> IsCropMode { get; } = new(false);

    public ReactivePropertySlim<bool> IsCameraMode { get; } = new(false);

    public ReactivePropertySlim<GizmoMode> SelectedGizmoMode { get; } = new(GizmoMode.Translate);

    public ReactivePropertySlim<Matrix> FrameMatrix { get; } = new(Matrix.Identity);

    public ReactiveProperty<UIToneMappingOperator> ToneMappingMode { get; }

    public ReactiveProperty<float> ToneMappingExposure { get; }

    public event EventHandler? PreviewInvalidated;

    // View側から設定
    public Size MaxFrameSize
    {
        get => _maxFrameSize;
        set
        {
            if (_maxFrameSize == value) return;
            _maxFrameSize = value;

            FrameCacheManager frameCacheManager = EditViewModel.FrameCacheManager.Value;
            var frameSize = frameCacheManager.FrameSize.ToSize(1);
            float scale = (float)Stretch.Uniform.CalculateScaling(_maxFrameSize, frameSize, StretchDirection.Both).X;
            if (scale != 0)
            {
                int den = (int)(1 / scale);
                if (den % 2 == 1)
                {
                    den++;
                }

                frameCacheManager.Options = frameCacheManager.Options with
                {
                    Size = PixelSize.FromSize(frameSize, 1f / den)
                };
            }
            else
            {
                frameCacheManager.Options = frameCacheManager.Options with { Size = null };
            }
        }
    }

    public Rect LastSelectedRect { get; set; }

    public EditViewModel EditViewModel => _editViewModel;

    public PathEditorViewModel PathEditor { get; }

    public void Play()
    {
        if (IsPlaying.Value) return;

        PlaybackSpeed.Value = 1.0f;
        PlaybackDirection.Value = ViewModels.PlaybackDirection.Forward;

        _playbackTask = Task.Run(async () =>
        {
            // ループ再生時は一度の Play() タスク内で再開する。
            // Post で Play() を再帰呼び出しすると _playbackTask が新しいタスクで上書きされ、
            // Pause() の `await _playbackTask;` が想定外のタスクを待ってしまうため避ける。
            bool restart;
            do
            {
                restart = await PlayInternal();
            } while (restart);
        });
    }

    private async Task<bool> PlayInternal()
    {
        if (!_isEnabled.Value || Scene == null)
            return false;

        BufferStatusViewModel bufferStatus = EditViewModel.BufferStatus;
        FrameCacheManager frameCacheManager = EditViewModel.FrameCacheManager.Value;
        Scene.Edited -= OnSceneEdited;
        _currentFrameSubscription?.Dispose();

        IsPlaying.Value = true;
        int rate = GetFrameRate();

        TimeSpan tick = TimeSpan.FromSeconds(1d / rate);
        TimeSpan startTime = _editorClock.CurrentTime.Value;
        TimeSpan durationTime = Scene.Duration;
        int startFrame = (int)startTime.ToFrameNumber(rate);
        int durationFrame = (int)Math.Ceiling(durationTime.ToFrameNumber(rate));
        int endFrame = (int)Scene.Start.ToFrameNumber(rate) + durationFrame;
        bufferStatus.StartTime.Value = startTime;
        bufferStatus.EndTime.Value = startTime;
        frameCacheManager.Options = frameCacheManager.Options with
        {
            DeletionStrategy = FrameCacheDeletionStrategy.BackwardBlock
        };

        frameCacheManager.CurrentFrame = startFrame;
        using var playerImpl = new BufferedPlayer(EditViewModel, Scene, IsPlaying, rate);
        _logger.LogInformation("Start the playback. ({SceneId}, {Rate}, {Start}, {Duration})",
            _editViewModel.SceneId, rate, startFrame, durationFrame);
        playerImpl.Start();

        var clock = new AudioPlaybackClock();
        var audioTask = PlayAudio(Scene, clock, startTime);

        // 音声バッファの準備に1フレーム以上かかると、映像だけが先に進んでしまい、
        // その後音声がアンカーされた瞬間に「映像が先行した状態」で止まってしまう。
        // 音声側が再生を開始（または音声なしと判明して終了）するまで待ってから
        // ウォールクロックの基準点を取得する。
        await clock.StartedTask;

        DateTime startDateTime = DateTime.UtcNow;
        var tcs = new TaskCompletionSource<bool>();
        int nextExpectedFrame = startFrame + 1;
        int processing = 0;
        bool reachedNaturalEnd = false;

        int ComputeExpectFrame()
        {
            TimeSpan elapsed = clock.GetTime() is { } audioTime
                ? audioTime - startTime
                : DateTime.UtcNow - startDateTime;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            return (int)(elapsed.Ticks / tick.Ticks) + startFrame;
        }

        await using var timer = new Timer(_ =>
        {
            if (Interlocked.Exchange(ref processing, 1) != 0) return;
            try
            {
                var expectFrame = ComputeExpectFrame();
                if (!IsPlaying.Value || expectFrame >= endFrame)
                {
                    // ループ用に endFrame で打ち切る場合、音声側にも停止を伝える
                    if (IsLoopEnabled.Value && expectFrame >= endFrame)
                    {
                        reachedNaturalEnd = true;
                        IsPlaying.Value = false;
                    }
                    tcs.TrySetResult(true);
                    return;
                }

                if (expectFrame < nextExpectedFrame)
                {
                    return;
                }

                while (playerImpl.TryDequeue(out IPlayer.Frame frame))
                {
                    using (frame.Bitmap)
                    {
                        UpdateImage(frame.Bitmap.Clone());

                        if (Scene != null)
                        {
                            _editorClock.CurrentTime.Value = frame.Time.ToTimeSpan(rate);
                            EditViewModel.FrameCacheManager.Value.CurrentFrame = frame.Time;
                        }
                    }

                    // タイマーが正確じゃないから、だんだんとフレームがずれてくる
                    // そのため、フレームを消費しすぎたら、そのフレーム番号とexpectFrameが一致するまでスキップする
                    // 逆に、フレームを消費しすぎない場合は、そのまま次のフレームを取得する
                    if (expectFrame <= frame.Time)
                    {
                        nextExpectedFrame = frame.Time + 1;
                        break;
                    }

                    // 期待していたフレームよりも前のフレームが来た場合
                }

                playerImpl.Skipped(ComputeExpectFrame() + 1);
            }
            finally
            {
                Interlocked.Exchange(ref processing, 0);
            }
        }, null, tick, tick);

        await Task.WhenAll(tcs.Task, audioTask);

        IsPlaying.Value = false;
        frameCacheManager.UpdateBlocks();
        bufferStatus.StartTime.Value = TimeSpan.Zero;
        bufferStatus.EndTime.Value = TimeSpan.Zero;
        frameCacheManager.Options = frameCacheManager.Options with
        {
            DeletionStrategy = FrameCacheDeletionStrategy.Old
        };

        _currentFrameSubscription = CurrentFrame.Subscribe(UpdateCurrentFrame);
        Scene.Edited += OnSceneEdited;
        _logger.LogInformation("End the playback. ({SceneId})", _editViewModel.SceneId);

        // ループが有効でユーザーによる停止ではない場合、ループ先頭に戻して再開を要求。
        // loopStart は購読で最新化されているため、再生中の In/Out 変更にも追従する。
        if (IsLoopEnabled.Value && reachedNaturalEnd && Scene != null)
        {
            _editorClock.CurrentTime.Value = Scene.Start;
            return true;
        }

        return false;
    }

    public int GetFrameRate()
    {
        int rate = Project?.GetFrameRate() ?? 30;
        if (rate <= 0)
        {
            rate = 30;
        }

        return rate;
    }

    public (TimeSpan Start, TimeSpan End) GetLoopRange()
    {
        if (Scene == null)
            return (TimeSpan.Zero, TimeSpan.Zero);

        return (Scene.Start, Scene.Start + Scene.Duration);
    }

    public void ToggleLoop()
    {
        IsLoopEnabled.Value = !IsLoopEnabled.Value;
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
        if (!_isEnabled.Value || Scene == null) return;
        var newDirection = forward
            ? ViewModels.PlaybackDirection.Forward
            : ViewModels.PlaybackDirection.Backward;

        // 通常再生中(L 押下時)はネイティブの再生を加速モードに切り替えるためまず停止
        if (IsPlaying.Value && !_isShuttling)
        {
            await Pause();
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
        if (_isShuttling || Scene == null) return;
        _isShuttling = true;
        IsPlaying.Value = true;

        _playbackTask = Task.Run(async () =>
        {
            try
            {
                int rate = GetFrameRate();
                TimeSpan tick = TimeSpan.FromSeconds(1d / rate);

                Scene.Edited -= OnSceneEdited;
                // 既存の CurrentFrame 購読は維持して UpdateCurrentFrame 経由でレンダリングを行う

                DateTime lastTime = DateTime.UtcNow;
                while (_isShuttling && IsPlaying.Value && Scene != null)
                {
                    var direction = PlaybackDirection.Value;
                    if (direction == ViewModels.PlaybackDirection.Stopped)
                        break;

                    // シャトル再生中に Scene.Start / Duration が変更されても追従できるよう毎回取得
                    TimeSpan sceneStart = Scene.Start;
                    TimeSpan sceneEnd = sceneStart + Scene.Duration;

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
                        if (Scene != null)
                        {
                            _editorClock.CurrentTime.Value = target;
                        }
                    });

                    await Task.Delay(tick).ConfigureAwait(false);
                }
            }
            finally
            {
                _isShuttling = false;
                IsPlaying.Value = false;
                PlaybackDirection.Value = ViewModels.PlaybackDirection.Stopped;
                PlaybackSpeed.Value = 1.0f;
                if (Scene != null)
                {
                    Scene.Edited += OnSceneEdited;
                }
            }
        });
    }

    private async Task PlayAudio(Scene scene, AudioPlaybackClock clock, TimeSpan startTime)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var audioContext = new XAudioContext();
                await PlayWithXA2(audioContext, scene, clock, startTime).ConfigureAwait(false);
            }
            else
            {
                await Task.Run(async () =>
                {
                    using var audioContext = new AudioContext();
                    await PlayWithOpenAL(audioContext, scene, clock, startTime);
                });
            }
        }
        catch (Exception ex)
        {
            NotificationService.ShowError(MessageStrings.UnexpectedError,
                MessageStrings.AudioPlaybackException);
            _logger.LogError(ex, "An exception occurred during audio playback.");
            IsPlaying.Value = false;
        }
        finally
        {
            clock.Pause();
        }
    }

    private static Pcm<Stereo32BitFloat>? FillAudioData(
        TimeSpan f, TimeSpan sceneEndTime, SceneComposer composer)
    {
        return ComposeThread.Dispatcher.Invoke(() =>
        {
            if (composer.Compose(new TimeRange(f, TimeSpan.FromSeconds(1))) is { } audio)
            {
                var pcm = audio.ToPcm();
                audio.Dispose();
                SilenceTailBeyondSceneEnd(pcm, f, sceneEndTime);
                return pcm;
            }
            else
            {
                return null;
            }
        });
    }

    // Scene.Start + Duration を超えた末尾サンプルをゼロ埋めする。
    // 編集中に Duration が縮んでバッファ全体が範囲外になるケースもありうる。
    private static void SilenceTailBeyondSceneEnd(
        Pcm<Stereo32BitFloat> pcm, TimeSpan bufferStart, TimeSpan sceneEndTime)
    {
        if (sceneEndTime <= bufferStart)
        {
            pcm.DataSpan.Clear();
            return;
        }

        TimeSpan bufferEnd = bufferStart + pcm.Duration;
        if (bufferEnd <= sceneEndTime) return;

        double keepSeconds = (sceneEndTime - bufferStart).TotalSeconds;
        int keepSamples = (int)Math.Ceiling(keepSeconds * pcm.SampleRate);
        keepSamples = Math.Clamp(keepSamples, 0, pcm.NumSamples);

        if (keepSamples < pcm.NumSamples)
        {
            pcm.DataSpan[keepSamples..].Clear();
        }
    }

    private static void Swap<T>(ref T x, ref T y)
    {
        (y, x) = (x, y);
    }

    // オーディオバックエンドが最初のサンプルを出力するまで短いポーリングで待機する。
    // true を返した場合は実サンプルの観測に成功しており、呼び出し側は安全に AnchorClock できる。
    // 期限内に進行が観測できなかった場合は false を返し、呼び出し側は音声クロックを諦めて
    // 壁時計にフォールバックする (サスペンド/切断中のデバイスで無期限ハングするのを防ぐため)。
    private static async Task<bool> WaitForFirstSampleAsync(
        Func<bool> hasProgressed, bool hasAudio, CancellationToken token)
    {
        if (!hasAudio) return false;
        long deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2; // 2s
        while (!token.IsCancellationRequested)
        {
            if (hasProgressed()) return true;
            if (Stopwatch.GetTimestamp() >= deadline) return false;
            try
            {
                await Task.Delay(1, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
        return false;
    }

    private async Task PlayWithXA2(XAudioContext audioContext, Scene scene,
        AudioPlaybackClock clock, TimeSpan startTime)
    {
        var composer = EditViewModel.Composer.Value;
        int sampleRate = composer.SampleRate;
        TimeSpan cur = startTime;
        var fmt = new WaveFormat(sampleRate, 32, 2);
        var source = new XAudioSource(audioContext);
        var primaryBuffer = new XAudioBuffer();
        var secondaryBuffer = new XAudioBuffer();
        bool hasAudio = false;
        bool audioClockValid = false;

        void PrepareBuffer(XAudioBuffer buffer)
        {
            TimeSpan bufferStartTime = cur;
            TimeSpan sceneEndTime = scene.Start + scene.Duration;
            Pcm<Stereo32BitFloat>? pcm = FillAudioData(cur, sceneEndTime, composer);
            if (pcm != null)
            {
                if (!hasAudio)
                {
                    sampleRate = pcm.SampleRate;
                    fmt = new WaveFormat(sampleRate, 32, 2);
                }

                buffer.BufferData(pcm.DataSpan, fmt);
                hasAudio = true;
                PublishAudioSnapshot(pcm, bufferStartTime);
            }

            source.QueueBuffer(buffer);
        }

        void AnchorClock()
        {
            if (!hasAudio || !audioClockValid) return;
            double seconds = (double)source.SamplesPlayed / sampleRate;
            clock.Anchor(startTime + TimeSpan.FromSeconds(seconds));
        }

        var cts = new CancellationTokenSource();
        IDisposable revoker = IsPlaying.Where(v => !v)
            .Take(1)
            .Subscribe(_ =>
            {
                // ReSharper disable AccessToDisposedClosure
                source.Stop();
                cts.Cancel();
                // ReSharper restore AccessToDisposedClosure
            });

        try
        {
            PrepareBuffer(primaryBuffer);

            cur += s_second;
            PrepareBuffer(secondaryBuffer);

            source.Play();
            // Play() 直後は SamplesPlayed が 0 のままで、その時点でアンカーすると
            // 映像がウォールクロックで先行し、後続の AnchorClock で巻き戻しが発生する。
            // 実際のサンプル出力が始まるのを観測してからアンカーする。
            // タイムアウトした場合はバックエンドがハングしている可能性が高いので、
            // 音声クロックを諦めて壁時計にフォールバックする。
            audioClockValid = await WaitForFirstSampleAsync(
                    () => source.SamplesPlayed > 0, hasAudio, cts.Token)
                .ConfigureAwait(false);
            if (hasAudio && !audioClockValid)
            {
                _logger.LogWarning(
                    "XAudio2 backend did not advance SamplesPlayed within the startup deadline; falling back to wall-clock timing.");
            }
            AnchorClock();
            // 壁時計フォールバック時や音声なしの場合は AnchorClock が no-op のため、
            // ここで明示的に StartedTask をシグナルする。
            clock.SignalStarted();

            await Task.Delay(1000, cts.Token).ConfigureAwait(false);
            AnchorClock();

            // primaryBufferが終了、secondaryが開始

            // 再生中に Scene.Start / Duration が編集される可能性があるため毎回再評価する
            // (ShuttleCore と同じポリシー)
            while (cur < scene.Start + scene.Duration)
            {
                if (!IsPlaying.Value)
                {
                    source.Stop();
                    break;
                }

                cur += s_second;

                PrepareBuffer(primaryBuffer);

                // バッファを入れ替える
                Swap(ref primaryBuffer, ref secondaryBuffer);

                await Task.Delay(1000, cts.Token).ConfigureAwait(false);
                AnchorClock();
            }
        }
        catch (OperationCanceledException)
        {
            source.Stop();
        }
        finally
        {
            revoker.Dispose();
            source.Dispose();
            cts.Dispose();
            primaryBuffer.Dispose();
            secondaryBuffer.Dispose();
        }
    }

    private async Task PlayWithOpenAL(AudioContext audioContext, Scene scene,
        AudioPlaybackClock clock, TimeSpan startTime)
    {
        var cts = new CancellationTokenSource();
        IDisposable revoker = IsPlaying.Where(v => !v)
            .Take(1)
            .Subscribe(_ => cts.Cancel());

        try
        {
            audioContext.MakeCurrent();

            var composer = EditViewModel.Composer.Value;
            int sampleRate = composer.SampleRate;
            TimeSpan cur = startTime;
            uint[] buffers = audioContext.GenBuffers(2);
            uint source = audioContext.GenSource();
            long totalProcessedSamples = 0;
            var queuedBufferSamples = new Queue<int>();
            bool hasAudio = false;
            bool audioClockValid = false;

            void AnchorClock()
            {
                if (!hasAudio || !audioClockValid) return;
                audioContext.GetSource(source, GetSourceInteger.SampleOffset, out int sampleOffset);
                long pos = totalProcessedSamples + sampleOffset;
                double seconds = (double)pos / sampleRate;
                clock.Anchor(startTime + TimeSpan.FromSeconds(seconds));
            }

            foreach (uint buffer in buffers)
            {
                TimeSpan bufferStartTime = cur;
                TimeSpan sceneEndTime = scene.Start + scene.Duration;
                using Pcm<Stereo32BitFloat>? pcmf = FillAudioData(cur, sceneEndTime, composer);
                cur += s_second;
                int fillSamples = 0;
                if (pcmf != null)
                {
                    using Pcm<Stereo16BitInteger> pcm = pcmf.Convert<Stereo16BitInteger>();

                    if (!hasAudio)
                    {
                        sampleRate = pcm.SampleRate;
                    }

                    audioContext.BufferData(buffer, BufferFormat.Stereo16, pcm.DataSpan, pcm.SampleRate);
                    fillSamples = pcm.DataSpan.Length;
                    hasAudio = true;
                    PublishAudioSnapshot(pcmf, bufferStartTime);
                }

                audioContext.SourceQueueBuffer(source, buffer);
                queuedBufferSamples.Enqueue(fillSamples);
            }

            audioContext.SourcePlay(source);

            try
            {
                // SourcePlay() 直後は SampleOffset が 0 のままで、その時点でアンカーすると
                // 映像がウォールクロックで先行し、後続の AnchorClock で巻き戻しが発生する。
                // 実際のサンプル出力が始まるのを観測してからアンカーする。
                // タイムアウトした場合はバックエンドがハングしている可能性が高いので、
                // 音声クロックを諦めて壁時計にフォールバックする。
                audioClockValid = await WaitForFirstSampleAsync(
                        () =>
                        {
                            audioContext.MakeCurrent();
                            audioContext.GetSource(source, GetSourceInteger.SampleOffset, out int offset);
                            return offset > 0;
                        },
                        hasAudio,
                        cts.Token)
                    .ConfigureAwait(false);
                // await 後は別のプールスレッドで継続する可能性があり、
                // OpenAL コンテキストはスレッド固有のため再バインドが必要。
                audioContext.MakeCurrent();
                if (hasAudio && !audioClockValid)
                {
                    _logger.LogWarning(
                        "OpenAL backend did not advance SampleOffset within the startup deadline; falling back to wall-clock timing.");
                }
                AnchorClock();
                // 壁時計フォールバック時や音声なしの場合は AnchorClock が no-op のため、
                // ここで明示的に StartedTask をシグナルする。
                clock.SignalStarted();

                while (IsPlaying.Value)
                {
                    audioContext.MakeCurrent();
                    audioContext.GetSource(source, GetSourceInteger.BuffersProcessed, out int processed);
                    while (processed > 0)
                    {
                        TimeSpan bufferStartTime = cur;
                        TimeSpan sceneEndTime = scene.Start + scene.Duration;
                        using Pcm<Stereo32BitFloat>? pcmf = FillAudioData(cur, sceneEndTime, composer);
                        cur += s_second;
                        uint buffer = audioContext.SourceUnqueueBuffer(source);
                        int fillSamples = 0;
                        if (pcmf != null)
                        {
                            using Pcm<Stereo16BitInteger> pcm = pcmf.Convert<Stereo16BitInteger>();

                            if (!hasAudio)
                            {
                                sampleRate = pcm.SampleRate;
                            }

                            audioContext.BufferData(buffer, BufferFormat.Stereo16, pcm.DataSpan, pcm.SampleRate);
                            fillSamples = pcm.DataSpan.Length;
                            hasAudio = true;
                            PublishAudioSnapshot(pcmf, bufferStartTime);
                        }

                        audioContext.SourceQueueBuffer(source, buffer);

                        totalProcessedSamples += queuedBufferSamples.Dequeue();
                        queuedBufferSamples.Enqueue(fillSamples);
                        processed--;
                    }

                    if (audioContext.GetSourceState(source) != SourceState.Playing)
                    {
                        audioContext.SourcePlay(source);
                    }

                    AnchorClock();

                    await Task.Delay(100, cts.Token).ConfigureAwait(false);
                    if (cur >= scene.Start + scene.Duration)
                        break;
                }

                while (audioContext.GetSourceState(source) == SourceState.Playing && IsPlaying.Value)
                {
                    audioContext.MakeCurrent();
                    audioContext.GetSource(source, GetSourceInteger.BuffersProcessed, out int drainProcessed);
                    while (drainProcessed > 0 && queuedBufferSamples.Count > 0)
                    {
                        audioContext.SourceUnqueueBuffer(source);
                        totalProcessedSamples += queuedBufferSamples.Dequeue();
                        drainProcessed--;
                    }
                    AnchorClock();
                    await Task.Delay(100, cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }

            audioContext.SourceStop(source);
            // https://hamken100.blogspot.com/2014/04/aldeletebuffersalinvalidoperation.html
            audioContext.Source(source, SourceInteger.Buffer, 0);
            audioContext.DeleteBuffers(buffers);
            audioContext.DeleteSource(source);
        }
        finally
        {
            revoker.Dispose();
        }
    }

    public async Task Pause()
    {
        if (!IsPlaying.Value) return;

        _logger.LogInformation("Pause the playback. ({SceneId})", _editViewModel.SceneId);
        _isShuttling = false;
        IsPlaying.Value = false;
        PlaybackSpeed.Value = 1.0f;
        PlaybackDirection.Value = ViewModels.PlaybackDirection.Stopped;
        await _playbackTask;
    }

    private void UpdateImage(Ref<Bitmap> source)
    {
        var oldBitmap = PreviewImage.Value;
        PreviewImage.Value = source;
        oldBitmap?.Dispose();

        PreviewInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void DrawBoundaries(Renderer renderer, SKCanvas canvas, Size canvasSize, bool recalculate = false)
    {
        int? selected = _editorSelection.SelectedLayerNumber.Value;
        if (selected.HasValue)
        {
            var frameSize = new Size(renderer.FrameSize.Width, renderer.FrameSize.Height);
            var frameScale = canvasSize.Width / frameSize.Width;
            float strokeScale = Stretch.Uniform.CalculateScaling(MaxFrameSize, frameSize).X;
            if (strokeScale < 1)
                strokeScale = 1;

            // フレームキャッシュを使う場合はBoundsを再計算する必要がある
            Rect[] boundary = recalculate
                ? renderer.RecalculateBoundaries(selected.Value)
                : renderer.GetBoundaries(selected.Value);
            if (boundary.Length > 0)
            {
                using var paint = new SKPaint
                {
                    Color = SKColors.White,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = strokeScale
                };
                bool exactBounds = GlobalConfiguration.Instance.ViewConfig.ShowExactBoundaries;

                foreach (Rect item in boundary)
                {
                    Rect rect = item;
                    if (!exactBounds)
                    {
                        rect = item.Inflate(4 / strokeScale);
                    }

                    rect *= frameScale;
                    canvas.DrawRect(rect.ToSKRect(), paint);
                }
            }
        }
    }

    private void QueueRender()
    {
        if (EditViewModel.Renderer.Value.IsGraphicsRendering)
            return;

        void RenderOnRenderThread(CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return;

            RenderThread.Dispatcher.Dispatch(() =>
            {
                try
                {
                    SceneRenderer renderer = EditViewModel.Renderer.Value;
                    FrameCacheManager cacheManager = EditViewModel.FrameCacheManager.Value;
                    if (renderer is not { IsDisposed: false, IsGraphicsRendering: false })
                        return;

                    int rate = GetFrameRate();
                    TimeSpan time = _editorClock.CurrentTime.Value;
                    int frame = (int)Math.Round(time.ToFrameNumber(rate), MidpointRounding.AwayFromZero);
                    time = frame.ToTimeSpan(rate);
                    Ref<Bitmap>? bitmapRef;

                    if (cacheManager.TryGet(frame, out var cache))
                    {
                        using (cache)
                        {
                            var compositionFrame = renderer.Compositor.EvaluateGraphics(time);

                            renderer.UpdateFrame(compositionFrame);
                            var bitmap = cache.Value.Clone();
                            bitmapRef = Ref<Bitmap>.Create(bitmap);

                            using (var canvas = new SKCanvas(bitmap.SKBitmap))
                            {
                                DrawBoundaries(renderer, canvas, new(bitmap.Width, bitmap.Height), true);
                            }
                        }
                    }
                    else
                    {
                        var compositionFrame = renderer.Compositor.EvaluateGraphics(time);
                        renderer.Render(compositionFrame);
                        var bitmap = renderer.Snapshot();
                        bitmapRef = Ref<Bitmap>.Create(bitmap);

                        if (cacheManager.IsEnabled)
                        {
                            cacheManager.Add(frame, bitmapRef);
                            cacheManager.UpdateBlocks();
                        }

                        using (var canvas = new SKCanvas(bitmap.SKBitmap))
                        {
                            DrawBoundaries(renderer, canvas, new(bitmap.Width, bitmap.Height), true);
                        }
                    }

                    UpdateImage(bitmapRef);

                    AfterRendered.OnNext(Unit.Default);
                }
                catch (Exception ex)
                {
                    NotificationService.ShowError(MessageStrings.UnexpectedError,
                        MessageStrings.FrameDrawingException);
                    _logger.LogError(ex, "An exception occurred while drawing the frame.");
                }
            }, ct: token);
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            () => RenderOnRenderThread(_cts.Token),
            Avalonia.Threading.DispatcherPriority.Background,
            _cts.Token);
    }

    private void UpdateCurrentFrame(TimeSpan timeSpan)
    {
        QueueRender();

        if (Scene == null) return;

        if (_editorClock.CurrentTime.Value != timeSpan)
        {
            //int rate = Project.GetFrameRate();
            //timeSpan = timeSpan.FloorToRate(rate);

            //if (timeSpan >= Scene.Duration)
            //{
            //    timeSpan = Scene.Duration - TimeSpan.FromSeconds(1d / rate);
            //}
            //if (timeSpan < TimeSpan.Zero)
            //{
            //    timeSpan = TimeSpan.Zero;
            //}

            _editorClock.CurrentTime.Value = timeSpan;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing PlayerViewModel. ({SceneId})", _editViewModel.SceneId);
        await Pause();
        Scene!.Edited -= OnSceneEdited;
        _disposables.Dispose();
        _currentFrameSubscription?.Dispose();
        AfterRendered.Dispose();
        _audioFramePushed.Dispose();
        PreviewInvalidated = null;
        Scene = null!;
        PreviewImage.Value?.Dispose();
        PreviewImage.Value = null!;
        _logger.LogInformation("Disposed PlayerViewModel. ({SceneId})", _editViewModel.SceneId);
    }

    public async Task<Rect> StartSelectRect()
    {
        TcsForCrop = new TaskCompletionSource<Rect>();
        IsCropMode.Value = true;
        Rect r = await TcsForCrop.Task;
        TcsForCrop = null;
        return r;
    }

    public TaskCompletionSource<Rect>? TcsForCrop { get; private set; }

    public async Task<Bitmap> DrawSelectedDrawable(Drawable drawable)
    {
        await Pause();

        return await RenderThread.Dispatcher.InvokeAsync(() =>
        {
            if (Scene == null) throw new Exception("Scene is null.");
            // TODO: Rendererに特定のDrawableのみを描画するクラスを追加する
            SceneRenderer renderer = EditViewModel.Renderer.Value;
            var resource = drawable.ToResource(new CompositionContext(CurrentFrame.Value));
            PixelSize frameSize = renderer.FrameSize;
            using var root = new DrawableRenderNode(resource);
            using (var context = new GraphicsContext2D(root, frameSize))
            {
                drawable.Render(context, resource);
            }

            var processor = new RenderNodeProcessor(root, false);
            return processor.RasterizeAndConcat();
        });
    }

    public async Task<Bitmap> DrawFrame()
    {
        await Pause();

        return await RenderThread.Dispatcher.InvokeAsync(() =>
        {
            if (Scene == null) throw new Exception("Scene is null.");
            SceneRenderer renderer = EditViewModel.Renderer.Value;

            RenderCacheOptions restoreCacheOptions = renderer.CacheOptions;
            renderer.CacheOptions = RenderCacheOptions.Disabled;

            try
            {
                var compositionFrame = renderer.Compositor.EvaluateGraphics(CurrentFrame.Value);
                renderer.Render(compositionFrame);

                return renderer.Snapshot();
            }
            finally
            {
                renderer.CacheOptions = restoreCacheOptions;
            }
        });
    }
}
