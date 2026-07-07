using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Platforms.XAudio2;
using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Editor.Components.PreviewSettingsTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Models;
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
    // Upper bound on how long Pause() waits for the playback loop to stop. If the loop is stuck
    // in a blocking OS audio/COM call, an unbounded await would hold the history-mutation gate
    // (and the UI thread awaiting it) indefinitely, so we time out and abandon the task instead.
    private static readonly TimeSpan s_pauseTimeout = TimeSpan.FromSeconds(5);
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
    private readonly PlaybackSessionGuard _sessionGuard = new();
    // Serializes RestoreStoppedPreviewState so PlayInternal's finally and Pause()'s timeout path
    // cannot interleave the dispose/resubscribe and Scene.Edited unhook/hook steps across threads.
    private readonly object _restoreLock = new();

    // Set by Pause(), cleared by Play(). Cancels a loop re-arm when a pause lands in the
    // brief IsPlaying=false window at a loop boundary that gating on IsPlaying would miss.
    private volatile bool _stopRequested;
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

        // Reapply panel-derived cache size to every rebuilt FrameCacheManager instance.
        editViewModel.FrameCacheManager
            .Skip(1)
            .Subscribe(ApplyMaxFrameSizeToCacheOptions)
            .DisposeWith(_disposables);

        // Re-render when the (Renderer, FrameCacheManager) pair is rebuilt. Triggering on cache
        // (derived from Renderer) ensures both halves are coherent when the work-item reads them.
        editViewModel.FrameCacheManager
            .Skip(1)
            .Subscribe(_ => QueueRender())
            .DisposeWith(_disposables);

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

        EditorConfig editorConfig = GlobalConfiguration.Instance.EditorConfig;

        // The settings UI now lives in PreviewSettingsTab; observe EditorConfig directly so the
        // preview re-renders on any onion-skin setting change.
        editorConfig.GetObservable(EditorConfig.IsOnionSkinEnabledProperty)
            .CombineLatest(
                editorConfig.GetObservable(EditorConfig.OnionSkinPrevCountProperty),
                editorConfig.GetObservable(EditorConfig.OnionSkinNextCountProperty),
                editorConfig.GetObservable(EditorConfig.OnionSkinPrevOpacityProperty),
                editorConfig.GetObservable(EditorConfig.OnionSkinNextOpacityProperty))
            .Skip(1)
            .Subscribe(_ =>
            {
                if (!IsPlaying.Value)
                {
                    QueueRender();
                }
            })
            .DisposeWith(_disposables);

        OpenPreviewSettings = new ReactiveCommand()
            .WithSubscribe(() =>
            {
                if (_editViewModel.FindToolTab<PreviewSettingsTabViewModel>() is { } tab)
                {
                    tab.IsSelected.Value = true;
                }
                else
                {
                    _editViewModel.OpenToolTab(new PreviewSettingsTabViewModel(_editViewModel));
                }
            })
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
        // Scene raises Edited synchronously with no thread guarantee, so marshal onto the UI thread
        // before HandleSceneEdited reads the EditorConfig CoreProperty getters: those hit CoreObject's
        // non-synchronized value dictionary and would race the UI-thread write-back if read off-thread.
        // This mirrors QueueRender, which already snapshots the same config on the UI thread; keeping
        // both paths consistent is defense-in-depth, not a fix for a known off-thread caller.
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            HandleSceneEdited(e);
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => HandleSceneEdited(e),
                Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    private void HandleSceneEdited(EventArgs e)
    {
        // Runs on the UI thread (directly or via the OnSceneEdited post). A Background-priority post
        // can still be pumped after DisposeAsync nulls Scene and unsubscribes, so bail out once the
        // view model is torn down (mirrors the Scene null check on the ShuttleCore post below).
        if (Scene is null)
        {
            return;
        }

        // IsPlaying and the onion-skin config are read here, at handling time, so the off-thread
        // post observes current state rather than whatever was live when the edit was raised.
        if (e is ElementEditedEventArgs elementEdited
            && !IsEditAffectingPreview(elementEdited.AffectedRange))
        {
            return;
        }

        QueueRender();
    }

    // The preview only needs to re-render when an edit touches a currently visible frame.
    // Normally that is just the playhead frame, but while the onion-skin overlay is active the
    // neighboring sample frames are visible too, so an edit confined to one of them must still
    // invalidate the preview. Must run on the UI thread (see OnSceneEdited).
    private bool IsEditAffectingPreview(IReadOnlyList<TimeRange> affectedRange)
    {
        EditorConfig editorConfig = GlobalConfiguration.Instance.EditorConfig;
        Scene? scene = Scene;
        bool onionSkinEnabled = editorConfig.IsOnionSkinEnabled && !IsPlaying.Value && scene is not null;

        return OnionSkinHelper.IsEditAffectingPreview(
            affectedRange,
            _editorClock.CurrentTime.Value,
            onionSkinEnabled,
            editorConfig.OnionSkinPrevCount, editorConfig.OnionSkinPrevOpacity,
            editorConfig.OnionSkinNextCount, editorConfig.OnionSkinNextOpacity,
            GetFrameRate(),
            scene?.Start ?? default, scene?.Duration ?? default);
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
            // The UI thread can dispose this composer (rebuild-by-replacement on frame-size changes)
            // after the calling-thread check above, so re-check on the compose thread and report
            // "no audio" instead of racing a disposed compositor.
            if (composer.IsDisposed)
                return (AudioFrameSnapshot?)null;

            AudioBuffer? audio;
            try
            {
                audio = composer.Compose(new TimeRange(start, duration));
            }
            catch (ObjectDisposedException)
            {
                // TOCTOU: the UI thread can dispose the composer between the IsDisposed re-check above
                // and this call (lockless rebuild-by-replacement on a frame-size change). Degrade to
                // "no audio" rather than surfacing the race as a throw on a throwaway background compose.
                return (AudioFrameSnapshot?)null;
            }

            using (audio)
            {
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
            }
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

    public ReactiveCommand OpenPreviewSettings { get; }

    public event EventHandler? PreviewInvalidated;

    // View側から設定、物理ピクセル
    public Size MaxFrameSize
    {
        get => _maxFrameSize;
        set
        {
            if (_maxFrameSize == value) return;
            _maxFrameSize = value;
            ApplyMaxFrameSizeToCacheOptions(EditViewModel.FrameCacheManager.Value);
        }
    }

    private void ApplyMaxFrameSizeToCacheOptions(FrameCacheManager frameCacheManager)
    {
        frameCacheManager.Options = frameCacheManager.Options with
        {
            Size = PreviewFrameCacheSizing.DeriveCacheSize(_maxFrameSize, frameCacheManager.FrameSize)
        };
    }

    public Rect LastSelectedRect { get; set; }

    public EditViewModel EditViewModel => _editViewModel;

    public PathEditorViewModel PathEditor { get; }

    public void Play()
    {
        if (IsPlaying.Value) return;
        if (!_isEnabled.Value || Scene == null) return;

        PlaybackSpeed.Value = 1.0f;
        PlaybackDirection.Value = ViewModels.PlaybackDirection.Forward;
        // Mark playing before publishing _playbackTask so a Pause() in the startup window
        // (before PlayInternal runs) signals the loop to stop instead of awaiting forever.
        _stopRequested = false;
        IsPlaying.Value = true;
        int generation = _sessionGuard.Claim();

        _playbackTask = Task.Run(async () =>
        {
            // ループ再生時は一度の Play() タスク内で再開する。
            // Post で Play() を再帰呼び出しすると _playbackTask が新しいタスクで上書きされ、
            // Pause() の `await _playbackTask;` が想定外のタスクを待ってしまうため避ける。
            bool restart;
            do
            {
                restart = await PlayInternal(generation);
                // Stop restarting on a boundary-window pause (_stopRequested set without flipping
                // IsPlaying), or when a Pause() timeout disowned this task and a newer session took
                // over — a stale task must not re-arm and stomp the session that replaced it.
                if (restart && (_stopRequested || !_sessionGuard.Owns(generation)))
                {
                    restart = false;
                }

                if (restart)
                {
                    // Re-arm IsPlaying for the next PlayInternal, which no longer sets it.
                    IsPlaying.Value = true;
                }
            } while (restart);
        });
    }

    private async Task<bool> PlayInternal(int generation)
    {
        if (!_isEnabled.Value || Scene == null)
        {
            IsPlaying.Value = false;
            return false;
        }

        BufferStatusViewModel bufferStatus = EditViewModel.BufferStatus;
        FrameCacheManager frameCacheManager = EditViewModel.FrameCacheManager.Value;
        Scene.Edited -= OnSceneEdited;
        _currentFrameSubscription?.Dispose();
        _currentFrameSubscription = null;

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
        bool reachedNaturalEnd = false;
        try
        {
            using var playerImpl = new BufferedPlayer(EditViewModel, Scene, IsPlaying, rate);
            _logger.LogInformation("Start the playback. ({SceneId}, {Rate}, {Start}, {Duration})",
                _editViewModel.SceneId, rate, startFrame, durationFrame);
            playerImpl.Start();

            var clock = new AudioPlaybackClock();
            var audioTask = PlayAudio(Scene, clock, startTime, generation);

            // 音声バッファの準備に1フレーム以上かかると、映像だけが先に進んでしまい、
            // その後音声がアンカーされた瞬間に「映像が先行した状態」で止まってしまう。
            // 音声側が再生を開始（または音声なしと判明して終了）するまで待ってから
            // ウォールクロックの基準点を取得する。
            await clock.StartedTask;

            DateTime startDateTime = DateTime.UtcNow;
            var tcs = new TaskCompletionSource<bool>();
            int nextExpectedFrame = startFrame + 1;
            int processing = 0;

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
                    // A Pause() timeout may have disowned this task while it is still blocked in the
                    // WhenAll below; once a newer session has claimed, stop the loop instead of
                    // dequeuing frames or writing shared preview state (PreviewImage / CurrentTime /
                    // IsPlaying) over the session that replaced it. The generation guard on the
                    // finally alone runs too late — the timer keeps firing until PlayInternal returns.
                    if (!_sessionGuard.Owns(generation))
                    {
                        tcs.TrySetResult(true);
                        return;
                    }

                    var expectFrame = ComputeExpectFrame();
                    if (_stopRequested || !IsPlaying.Value || expectFrame >= endFrame)
                    {
                        // ループ用に endFrame で打ち切る場合、音声側にも停止を伝える。
                        // ただし停止要求中はループの自然終端とみなさず、再開させない。
                        if (!_stopRequested && IsLoopEnabled.Value && expectFrame >= endFrame)
                        {
                            reachedNaturalEnd = true;
                            IsPlaying.Value = false;
                        }
                        else if (_stopRequested)
                        {
                            // A pause that raced the loop re-arm leaves IsPlaying=true; clear it so the
                            // audio task stops here instead of running to the scene's natural end.
                            IsPlaying.Value = false;
                        }

                        tcs.TrySetResult(true);
                        return;
                    }

                    if (expectFrame < nextExpectedFrame)
                    {
                        return;
                    }

                    bool dequeued = false;
                    while (playerImpl.TryDequeue(out IPlayer.Frame frame))
                    {
                        dequeued = true;
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

                    if (!dequeued && playerImpl.ProducerStopped)
                    {
                        IsPlaying.Value = false;
                        tcs.TrySetResult(true);
                        return;
                    }

                    playerImpl.Skipped(ComputeExpectFrame() + 1);
                }
                finally
                {
                    Interlocked.Exchange(ref processing, 0);
                }
            }, null, tick, tick);

            await Task.WhenAll(tcs.Task, audioTask);

            // Committing the recorded cache blocks is a shared write, so gate it on ownership like the
            // finally/rewind paths below: a task disowned by a Pause() timeout that unblocks after a
            // newer session started must not stomp that session's frame-cache bookkeeping.
            if (_sessionGuard.Owns(generation))
            {
                frameCacheManager.UpdateBlocks();
            }
        }
        finally
        {
            // Restore the stopped state (IsPlaying, buffer/cache, and the preview subscriptions this
            // task detached on entry) only while this task still owns the session. If a Pause()
            // timeout disowned it and a newer session took over, restoring here would stomp that
            // session, so the new owner — or Pause()'s timeout path — restores it instead.
            if (_sessionGuard.Owns(generation))
            {
                RestoreStoppedPreviewState();
            }

            _logger.LogInformation("End the playback. ({SceneId})", _editViewModel.SceneId);
        }

        // ループが有効でユーザーによる停止ではない場合、ループ先頭に戻して再開を要求。
        // loopStart は購読で最新化されているため、再生中の In/Out 変更にも追従する。
        // A task disowned by a Pause() timeout must not rewind the playhead of a stopped editor or
        // the session that replaced it, so gate the shared CurrentTime write on ownership too.
        if (IsLoopEnabled.Value && reachedNaturalEnd && Scene != null && _sessionGuard.Owns(generation))
        {
            _editorClock.CurrentTime.Value = Scene.Start;
            return true;
        }

        return false;
    }

    // Return the editor to a consistent stopped state: clear IsPlaying, reset the playback-only
    // buffer/frame-cache state, and re-attach the preview subscriptions a playback task detached on
    // entry. Idempotent (dispose-then-resubscribe, remove-then-add) so it stays correct even when
    // PlayInternal's finally and Pause()'s timeout path both run it around an abandoned task.
    private void RestoreStoppedPreviewState()
    {
        lock (_restoreLock)
        {
            IsPlaying.Value = false;
            BufferStatusViewModel bufferStatus = EditViewModel.BufferStatus;
            bufferStatus.StartTime.Value = TimeSpan.Zero;
            bufferStatus.EndTime.Value = TimeSpan.Zero;
            FrameCacheManager frameCacheManager = EditViewModel.FrameCacheManager.Value;
            frameCacheManager.Options = frameCacheManager.Options with
            {
                DeletionStrategy = FrameCacheDeletionStrategy.Old
            };

            _currentFrameSubscription?.Dispose();
            _currentFrameSubscription = CurrentFrame.Subscribe(UpdateCurrentFrame);
            if (Scene != null)
            {
                Scene.Edited -= OnSceneEdited;
                Scene.Edited += OnSceneEdited;
            }
        }
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

    public Subject<Unit> BeginEditTimecodeRequested { get; } = new();

    public void RequestEditTimecode()
    {
        BeginEditTimecodeRequested.OnNext(Unit.Default);
    }

    public bool TryParseTimecode(string input, out TimeSpan target, out GotoTimecodeError error)
    {
        target = TimeSpan.Zero;
        if (Scene == null)
        {
            // The view should not raise the event without a scene; surface the
            // state mismatch so it is visible in telemetry.
            _logger.LogWarning(
                "TryParseTimecode invoked with no scene loaded. ({SceneId}, Input={Input})",
                _editViewModel.SceneId, input);
            error = GotoTimecodeError.NoScene;
            return false;
        }

        int rate = GetFrameRate();
        if (!GotoTimecodeParser.TryParse(input, rate, _editorClock.CurrentTime.Value, Scene.Markers, out TimeSpan parsed, out error))
        {
            _logger.LogDebug(
                "Goto-timecode parse failed. ({SceneId}, Input={Input}, Error={Error})",
                _editViewModel.SceneId, input, error);
            return false;
        }

        // Frame-snap up front so ApplyTimecodeSeek cannot fail later — without
        // this, the parser-accepted value could be returned to the view, the
        // editor would close on Accept(), and the actual seek would silently
        // no-op when RoundToRate overflowed.
        try
        {
            target = parsed.RoundToRate(rate);
        }
        catch (OverflowException ex)
        {
            _logger.LogWarning(ex,
                "RoundToRate overflowed for goto-timecode target. ({SceneId}, Parsed={Parsed}, Rate={Rate})",
                _editViewModel.SceneId, parsed, rate);
            error = GotoTimecodeError.OutOfRange;
            return false;
        }

        TimeSpan endTime = Scene.Start + Scene.Duration - TimeSpan.FromSeconds(1d / rate);
        if (target < Scene.Start || target > endTime)
        {
            _logger.LogDebug(
                "Goto-timecode target out of scene range. ({SceneId}, Target={Target}, Range=[{Start}, {End}])",
                _editViewModel.SceneId, target, Scene.Start, endTime);
            error = GotoTimecodeError.OutOfRange;
            return false;
        }

        return true;
    }

    public void ApplyTimecodeSeek(TimeSpan target)
    {
        // target is already frame-snapped by TryParseTimecode.
        _editorClock.CurrentTime.Value = target;

        // 再生ヘッドがビューポート外へ飛ぶ場合に追従する。EditViewModel.CommandHandler の
        // 既存スクロール呼び出しと同形。
        if (_editViewModel.FindToolTab<TimelineTabViewModel>() is { } timeline)
        {
            int currentZIndex = timeline.ToLayerNumber(timeline.Options.Value.Offset.Y);
            timeline.ScrollTo.Execute(
                (new Beutl.Media.TimeRange(target, TimeSpan.FromTicks(1)), currentZIndex));
        }
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
        if (_isShuttling || Scene == null) return;
        // Clear a stop request left by a prior Pause() so the flag's "true until the next
        // playback start" invariant holds across shuttle too, not just Play().
        _stopRequested = false;
        _isShuttling = true;
        IsPlaying.Value = true;
        int generation = _sessionGuard.Claim();

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
                // Only restore shared state while this shuttle still owns the session; a Pause()
                // timeout that disowned it must not let this late finally stomp a newer session.
                if (_sessionGuard.Owns(generation))
                {
                    _isShuttling = false;
                    IsPlaying.Value = false;
                    PlaybackDirection.Value = ViewModels.PlaybackDirection.Stopped;
                    PlaybackSpeed.Value = 1.0f;
                    if (Scene != null)
                    {
                        Scene.Edited -= OnSceneEdited;
                        Scene.Edited += OnSceneEdited;
                    }
                }
            }
        });
    }

    private async Task PlayAudio(Scene scene, AudioPlaybackClock clock, TimeSpan startTime, int generation)
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
            // Only stop the session this task owns: a fault from an audio backend abandoned by a
            // Pause() timeout must not clear IsPlaying on the session that replaced it.
            if (_sessionGuard.Owns(generation))
            {
                IsPlaying.Value = false;
            }
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
        // Record the stop request even when IsPlaying is already false: at a loop boundary
        // the task clears IsPlaying before re-arming, and a pause in that window must still
        // cancel the pending restart.
        _stopRequested = true;
        if (IsPlaying.Value)
        {
            _logger.LogInformation("Pause the playback. ({SceneId})", _editViewModel.SceneId);
            _isShuttling = false;
            IsPlaying.Value = false;
            PlaybackSpeed.Value = 1.0f;
            PlaybackDirection.Value = ViewModels.PlaybackDirection.Stopped;
        }

        // Await even when already stopped so an overlapping pause blocks until the prior
        // drain finishes. Bound the wait: if the playback loop is stuck in a blocking OS
        // audio/COM call, awaiting it unbounded would pin the history-mutation gate (and the
        // UI thread awaiting Pause) indefinitely, so time out, log, and abandon the task.
        // Catch a faulted task and drop it, so it neither surfaces as a history-operation
        // failure nor replays on later pauses.
        Task playbackTask = _playbackTask;
        try
        {
            if (!await WaitForPlaybackStopAsync(playbackTask, s_pauseTimeout, _logger, _editViewModel.SceneId))
            {
                // Timed out: drop the hung task so it neither replays on later pauses nor keeps
                // the gate held. The abandoned task keeps observing _stopRequested / the cts.
                if (_playbackTask == playbackTask)
                {
                    _playbackTask = Task.CompletedTask;
                    // Disown the abandoned task so its late finally can't restore/stomp a future
                    // session, then restore the stopped state here: the task detached the preview
                    // subscriptions on entry and, now disowned, will skip re-attaching them, so
                    // otherwise the editor would stay detached until (if ever) the task unblocks.
                    _sessionGuard.Disown();
                    RestoreStoppedPreviewState();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Playback task faulted before pause. ({SceneId})", _editViewModel.SceneId);
            if (_playbackTask == playbackTask)
            {
                _playbackTask = Task.CompletedTask;
            }
        }
    }

    // Wait for the playback task to finish, but never longer than <paramref name="timeout"/>.
    // Returns true when the task completed (its fault, if any, is re-thrown to the caller);
    // false when the wait timed out. A timeout is logged and the task is left running but kept
    // observed via a continuation, so a playback loop blocked in a native audio/COM call cannot
    // pin the caller (and the history-mutation gate it runs under) indefinitely.
    internal static async Task<bool> WaitForPlaybackStopAsync(
        Task playbackTask, TimeSpan timeout, ILogger logger, string sceneId)
    {
        Task finished = await Task.WhenAny(playbackTask, Task.Delay(timeout)).ConfigureAwait(false);
        if (finished == playbackTask)
        {
            // Completed within the timeout — propagate any fault so the caller can observe it.
            await playbackTask.ConfigureAwait(false);
            return true;
        }

        logger.LogError(
            "Playback task did not stop within {Timeout} on pause; abandoning it to release the history gate. ({SceneId})",
            timeout, sceneId);
        // Observe a late fault so abandoning the task does not raise an unobserved-exception event.
        _ = playbackTask.ContinueWith(
            static (t, s) => ((ILogger)s!).LogError(
                t.Exception, "Abandoned playback task faulted after a pause timeout."),
            logger,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return false;
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

    // Re-render the current frame into the viewport. Used when something outside the normal edit /
    // playback path (e.g. a PreviewSourceMode switch) invalidates the shown frame while paused.
    public void QueuePreviewRender() => QueueRender();

    private void QueueRender()
    {
        if (EditViewModel.Renderer.Value.IsGraphicsRendering)
            return;

        void RenderOnRenderThread(CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return;

            // Snapshot the onion-skin config here on the UI thread (RenderOnRenderThread is
            // invoked via Dispatcher.UIThread). Reading these CoreProperty getters inside the
            // render-thread dispatch below would race the UI-thread write-back subscriptions
            // against CoreObject's non-synchronized value dictionary.
            EditorConfig editorConfig = GlobalConfiguration.Instance.EditorConfig;
            bool onionEnabled = editorConfig.IsOnionSkinEnabled;
            int onionPrevCount = editorConfig.OnionSkinPrevCount;
            int onionNextCount = editorConfig.OnionSkinNextCount;
            float onionPrevOpacity = editorConfig.OnionSkinPrevOpacity;
            float onionNextOpacity = editorConfig.OnionSkinNextOpacity;

            RenderThread.Dispatcher.Dispatch(() =>
            {
                int frame = 0;
                bool useOnionSkin = false;
                int onionSampleCount = 0;
                try
                {
                    SceneRenderer renderer = EditViewModel.Renderer.Value;
                    FrameCacheManager cacheManager = EditViewModel.FrameCacheManager.Value;
                    // Mid-swap the properties can briefly expose a disposed instance (the pair is
                    // replaced as two swaps, renderer first). Bail out — the cache swap queues a fresh
                    // render once both halves are in place.
                    if (renderer is not { IsDisposed: false, IsGraphicsRendering: false }
                        || cacheManager.IsDisposed)
                        return;
                    if (Scene is null)
                        return;

                    int rate = GetFrameRate();
                    TimeSpan time = _editorClock.CurrentTime.Value;
                    frame = (int)Math.Round(time.ToFrameNumber(rate), MidpointRounding.AwayFromZero);
                    time = frame.ToTimeSpan(rate);
                    Ref<Bitmap>? bitmapRef;

                    // A side contributes nothing when its opacity is 0, so fold opacity into the
                    // effective count. When both sides are invisible useOnionSkin stays false and
                    // the empty-samples fallback below routes through the cheaper cache-aware path.
                    int effectivePrevCount = onionPrevOpacity > 0f ? onionPrevCount : 0;
                    int effectiveNextCount = onionNextOpacity > 0f ? onionNextCount : 0;
                    useOnionSkin = onionEnabled
                        && !IsPlaying.Value
                        && (effectivePrevCount > 0 || effectiveNextCount > 0);

                    IReadOnlyList<OnionSkinSample> onionSamples = [];
                    if (useOnionSkin)
                    {
                        onionSamples = OnionSkinHelper.EnumerateOnionSkinTimes(
                            frame, Scene.Start, Scene.Duration, rate,
                            effectivePrevCount, effectiveNextCount,
                            onionPrevOpacity, onionNextOpacity);
                        onionSampleCount = onionSamples.Count;
                        if (onionSamples.Count == 0)
                        {
                            // Range clamp emptied everything — fall back to the cache-aware path
                            // instead of taking the heavier (cache-less) onion branch for nothing.
                            _logger.LogDebug(
                                "Onion skin enabled but no samples in range at frame {Frame}; using normal preview.",
                                frame);
                            useOnionSkin = false;
                        }
                    }

                    if (useOnionSkin)
                    {
                        // Render the current frame; its Snapshot bitmap doubles as the composition
                        // canvas. Onion-composited frames are intentionally NOT pushed into
                        // cacheManager: the cache is keyed on frame number alone, and an
                        // onion-disabled re-render would otherwise return the composite.
                        var currentCompositionFrame = renderer.Compositor.EvaluateGraphics(time);
                        renderer.Render(currentCompositionFrame);
                        Bitmap? currentBitmap = null;
                        Bitmap? onionScratch = null;

                        try
                        {
                            currentBitmap = renderer.Snapshot();
                            using (var canvas = new SKCanvas(currentBitmap.SKBitmap))
                            {
                                foreach (var sample in onionSamples)
                                {
                                    // A newer QueueRender has superseded this pass (e.g. the user
                                    // kept scrubbing); stop re-rendering samples instead of grinding
                                    // through all of them. Break (not return) so the playhead restore
                                    // and boundary draw below still run, leaving the renderer on the
                                    // current frame; the partial composite is replaced by the queued render.
                                    if (token.IsCancellationRequested)
                                        break;

                                    // Belt-and-suspenders for the mixed case (one side opaque, the
                                    // other at zero opacity): skip the render/snapshot/blend for any
                                    // sample that would composite nothing.
                                    if (sample.Alpha <= 0f)
                                        continue;

                                    var compFrame = renderer.Compositor.EvaluateGraphics(sample.Time);
                                    renderer.Render(compFrame);

                                    // Reuse one scratch bitmap across all onion samples (up to 20),
                                    // turning per-sample LOH allocations into a single one.
                                    // CreateSnapshotBitmap gives it the format SnapshotInto requires.
                                    onionScratch ??= renderer.CreateSnapshotBitmap();
                                    renderer.SnapshotInto(onionScratch);

                                    using var paint = new SKPaint
                                    {
                                        Color = new SKColor(255, 255, 255, (byte)Math.Round(sample.Alpha * 255)),
                                        BlendMode = SKBlendMode.SrcOver,
                                    };

                                    // canvas is a CPU raster SKCanvas, so DrawBitmap blends the scratch
                                    // pixels synchronously — safe to overwrite onionScratch next sample.
                                    canvas.DrawBitmap(onionScratch.SKBitmap, 0, 0, paint);
                                }

                                // Restore renderer entries to the playhead BEFORE drawing
                                // boundaries so the selection box uses the current frame's
                                // geometry, not the last onion sample's.
                                renderer.UpdateFrame(renderer.Compositor.EvaluateGraphics(time));

                                DrawBoundaries(renderer, canvas, new(currentBitmap.Width, currentBitmap.Height), true);
                            }

                            // Ownership moves to bitmapRef here; this is the last statement in the
                            // try, so the catch below only ever disposes currentBitmap on a path
                            // before this point (no double-dispose).
                            bitmapRef = Ref<Bitmap>.Create(currentBitmap);
                        }
                        catch
                        {
                            currentBitmap?.Dispose();
                            // Best-effort: restore the playhead even on failure so later
                            // HitTest / GetBoundary use the current frame, not a leftover onion
                            // sample. Guard the restore so it can't mask the original exception.
                            try
                            {
                                renderer.UpdateFrame(renderer.Compositor.EvaluateGraphics(time));
                            }
                            catch (Exception restoreEx)
                            {
                                _logger.LogError(restoreEx,
                                    "Failed to restore playhead after onion-skin render failure at frame {Frame}.",
                                    frame);
                            }

                            throw;
                        }
                        finally
                        {
                            onionScratch?.Dispose();
                        }
                    }
                    else if (cacheManager.TryGet(frame, out var cache))
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

                    // Dispose と並走した場合に AfterRendered が破棄されている可能性があるためトークンを確認する
                    if (!token.IsCancellationRequested)
                        AfterRendered.OnNext(Unit.Default);
                }
                catch (ObjectDisposedException) when (token.IsCancellationRequested)
                {
                    // Dispose 中のレースで AfterRendered などが破棄された場合のみ無視する。
                    // 通常再生中の予期しない ObjectDisposedException は下の catch で表面化させる。
                }
                catch (Exception ex)
                {
                    NotificationService.ShowError(MessageStrings.UnexpectedError,
                        MessageStrings.FrameDrawingException);
                    _logger.LogError(ex,
                        "An exception occurred while drawing the frame. onionSkin={UseOnionSkin}, sampleCount={Count}, frame={Frame}.",
                        useOnionSkin, onionSampleCount, frame);
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
        // 進行中の QueueRender をキャンセルしてから Subject を破棄し、レンダースレッドが
        // 破棄済みの AfterRendered/_audioFramePushed に OnNext しないようにする
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        Scene!.Edited -= OnSceneEdited;
        _disposables.Dispose();
        _currentFrameSubscription?.Dispose();
        AfterRendered.Dispose();
        _audioFramePushed.Dispose();
        BeginEditTimecodeRequested.Dispose();
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

    /// <summary>
    /// Measures the logical pixel size <paramref name="drawable"/> renders into at unit scale.
    /// </summary>
    public async Task<PixelSize> MeasureSelectedDrawable(Drawable drawable)
    {
        await Pause();

        return await RenderThread.Dispatcher.InvokeAsync(() =>
        {
            if (Scene == null) throw new Exception("Scene is null.");
            SceneRenderer renderer = EditViewModel.Renderer.Value;
            var resource = drawable.ToResource(new CompositionContext(CurrentFrame.Value));
            PixelSize frameSize = renderer.FrameSize;
            using var root = new DrawableRenderNode(resource);
            using (var context = new GraphicsContext2D(root, frameSize.ToSize(1)))
            {
                drawable.Render(context, resource);
            }

            var processor = new RenderNodeProcessor(root, false);
            var bounds = Rect.Empty;
            foreach (var op in processor.PullToRoot())
            {
                bounds = bounds.Union(op.Bounds);
                op.Dispose();
            }

            return PixelRect.FromRect(bounds).Size;
        });
    }

    /// <summary>
    /// Renders <paramref name="drawable"/> on its own at the given <paramref name="outputScale"/>.
    /// </summary>
    public async Task<Bitmap> DrawSelectedDrawable(Drawable drawable, float outputScale = 1f)
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
            using (var context = new GraphicsContext2D(root, frameSize.ToSize(1)))
            {
                drawable.Render(context, resource);
            }

            var processor = new RenderNodeProcessor(
                root, false, outputScale, WorkingScaleCeiling.Export());
            return processor.RasterizeAndConcat();
        });
    }

    /// <summary>
    /// Renders the current frame at full scale on a throwaway renderer, ignoring preview quality.
    /// </summary>
    public Task<Bitmap> DrawFrameAtFullScale() => DrawFrameAtScale(1f);

    /// <summary>
    /// Renders the current frame at <paramref name="outputScale"/> on a throwaway renderer,
    /// ignoring preview quality. The surface is <c>ceil(FrameSize * outputScale)</c>.
    /// </summary>
    public async Task<Bitmap> DrawFrameAtScale(float outputScale)
    {
        await Pause();

        return await RenderThread.Dispatcher.InvokeAsync(() =>
        {
            if (Scene == null) throw new Exception("Scene is null.");

            // Throwaway renderer with disableResourceShare to avoid mutating live preview resources.
            using var renderer = new SceneRenderer(
                Scene,
                renderScale: outputScale,
                disableResourceShare: true,
                maxWorkingScale: WorkingScaleCeiling.Export(),
                forceOriginalSource: true);
            renderer.CacheOptions = RenderCacheOptions.Disabled;

            var compositionFrame = renderer.Compositor.EvaluateGraphics(CurrentFrame.Value);
            renderer.Render(compositionFrame);

            // Surface is ceil(FrameSize * outputScale); return as-is.
            return renderer.Snapshot();
        });
    }
}
