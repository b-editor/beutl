using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Beutl.Audio.Composing;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Editor.Components.PreviewSettingsTab.ViewModels;
using Beutl.Editor.Models;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics3D.Gizmo;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Models;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public enum PlaybackDirection
{
    Stopped,
    Forward,
    Backward,
}

public sealed partial class PlayerViewModel : IAsyncDisposable, IPreviewPlayer, IFullScaleFrameRenderer
{
    private static readonly TimeSpan s_second = TimeSpan.FromSeconds(1);
    // Bound the native audio/backend wait. Scene composition and rendering are drained separately
    // before Pause completes: abandoning a backend task does not mean it has stopped reading the scene.
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
    private readonly object _renderRequestLock = new();
    private CancellationTokenSource? _cts;
    private volatile bool _isDisposing;
    private Size _maxFrameSize;
    private Task _playbackTask = Task.CompletedTask;
    private Task _pauseTask = Task.CompletedTask;
    private volatile bool _isPausing;
    private bool _isShuttling;
    private readonly PlaybackSessionGuard _sessionGuard = new();
    private readonly ReactivePropertySlim<string?> _previewRenderError = new();
    private long _previewRenderErrorIssuedVersion;
    private long _previewRenderErrorAppliedVersion;
    // Serializes RestoreStoppedPreviewState so PlayInternal's finally and Pause()'s timeout path
    // cannot interleave the dispose/resubscribe and Scene.Edited unhook/hook steps across threads.
    private readonly object _restoreLock = new();

    // Set by Pause(), cleared by Play(). Cancels a loop re-arm when a pause lands in the
    // brief IsPlaying=false window at a loop boundary that gating on IsPlaying would miss.
    private volatile bool _stopRequested;
    // A snapshot is published when its buffer is queued, ahead of the playhead, so the recent ones are
    // replayed to a visualizer tab opened mid-playback.
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
                _editorClock.Seek(
                    _editorClock.CurrentTime.Value > endTime
                        ? endTime
                        : _editorClock.CurrentTime.Value > Scene.Start
                            ? Scene.Start
                            : TimeSpan.Zero);
            })
            .DisposeWith(_disposables);

        End = new ReactiveCommand(_isEnabled)
            .WithSubscribe(() =>
            {
                int rate = GetFrameRate();
                var endTime = Scene.Start + Scene.Duration - TimeSpan.FromSeconds(1d / rate);
                _editorClock.Seek(
                    _editorClock.CurrentTime.Value < Scene.Start
                        ? Scene.Start
                        : _editorClock.CurrentTime.Value < endTime
                            ? endTime
                            : Scene.Children.Count > 0
                                ? Scene.Children.Max(i => i.Start + i.Length) - TimeSpan.FromSeconds(1d / rate)
                                : TimeSpan.Zero);
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

        IsCameraMode.Subscribe(isCameraMode =>
            {
                if (!isCameraMode)
                {
                    ClearAllGizmoTargets();
                }
            })
            .DisposeWith(_disposables);

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

        // Re-render when an onion-skin setting changes.
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
                    scene3D.GizmoTarget.Value = null;
                    scene3D.GizmoMode.Value = GizmoMode.None;
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
                     .Where(s => s.GizmoTarget.Value.HasValue))
        {
            scene3D.GizmoMode.Value = mode;
        }
    }

    private void OnSceneEdited(object? sender, EventArgs e)
    {
        // Edited has no thread guarantee, and HandleSceneEdited reads EditorConfig, whose values are not
        // synchronized, so it runs on the UI thread like QueueRender (a safeguard, not a known caller).
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

    public IReadOnlyReactiveProperty<string?> PreviewRenderError => _previewRenderError;

    IReadOnlyReactiveProperty<Ref<Bitmap>?> IPreviewPlayer.PreviewImage => PreviewImage;

    IObservable<Unit> IPreviewPlayer.AfterRendered => AfterRendered;

    IReadOnlyReactiveProperty<bool> IPreviewPlayer.IsPlaying => IsPlaying;

    IObservable<AudioFrameSnapshot> IPreviewPlayer.AudioFramePushed => _audioFramePushed;

    public ReactivePropertySlim<bool> IsPlaying { get; } = new();

    // The task spans loop re-arms and pause drains, where IsPlaying can already be false.
    internal bool IsPlaybackActive => IsPlaying.Value || _isPausing || !Volatile.Read(ref _playbackTask).IsCompleted;

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

    public EditViewModel EditViewModel => _editViewModel;

    public PathEditorViewModel PathEditor { get; }

    public void Play()
    {
        if (_isDisposing || _isPausing || IsPlaying.Value) return;
        if (!_isEnabled.Value || Scene == null) return;

        UsageTelemetry.Current?.Record("playback.started", feature: "normal");
        PlaybackSpeed.Value = 1.0f;
        PlaybackDirection.Value = ViewModels.PlaybackDirection.Forward;
        // Mark playing before publishing _playbackTask so a Pause() in the startup window
        // (before PlayInternal runs) signals the loop to stop instead of awaiting forever.
        int generation = _sessionGuard.Claim(() =>
        {
            _stopRequested = false;
            IsPlaying.Value = true;
        });

        _playbackTask = Task.Run(async () =>
        {
            // ループ再生時は一度の Play() タスク内で再開する。
            // Post で Play() を再帰呼び出しすると _playbackTask が新しいタスクで上書きされ、
            // Pause() の `await _playbackTask;` が想定外のタスクを待ってしまうため避ける。
            bool restart;
            do
            {
                using var playbackCts = new CancellationTokenSource();
                using IDisposable cancelPlayback = IsPlaying
                    .Where(static value => !value)
                    .Take(1)
                    .Subscribe(value => _ = CancelPlaybackAsync(playbackCts));
                restart = await PlayInternal(generation, playbackCts.Token);
                // Stop restarting on a boundary-window pause (_stopRequested set without flipping
                // IsPlaying), or when a Pause() timeout disowned this task and a newer session took
                // over — a stale task must not re-arm and stomp the session that replaced it.
                if (restart && (_isPausing || _stopRequested || !_sessionGuard.Owns(generation)))
                {
                    restart = false;
                }

                if (restart && !await TryApplyOnUIThreadAsync(generation, () => IsPlaying.Value = true))
                {
                    restart = false;
                }
            } while (restart);
        });
    }

    // Changes the playback state on the UI thread, where the editor's subscribers run, after whatever the
    // playback already posted there. False when the session no longer owns the state or a subscriber threw.
    private async Task<bool> TryApplyOnUIThreadAsync(int generation, Action update)
    {
        return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            () => TryApplyLogged(generation, update),
            Avalonia.Threading.DispatcherPriority.Normal);
    }

    // On the UI thread an exception that escapes a dispatcher job ends the process, so a subscriber that throws
    // is logged instead, and playback stops: a failed loop restart would otherwise leave IsPlaying set with
    // nothing playing, and Play() would refuse to start again.
    private bool TryApplyLogged(int generation, Action update)
    {
        try
        {
            return _sessionGuard.TryApply(generation, update);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred while changing the playback state. ({SceneId})",
                _editViewModel.SceneId);
            try
            {
                _sessionGuard.TryApply(generation, () => IsPlaying.Value = false);
            }
            catch (Exception stopEx)
            {
                _logger.LogError(stopEx, "An exception occurred while stopping the playback. ({SceneId})",
                    _editViewModel.SceneId);
            }

            return false;
        }
    }

    private async Task<bool> PlayInternal(int generation, CancellationToken playbackToken)
    {
        Scene scene = null!;
        FrameCacheManager frameCacheManager = null!;
        int rate = 0, sampleRate = 0;
        PlaybackRange range = default;
        TimeSpan startTime = default;
        bool ready = false;
        // Startup can resume after a timeout or disposal. Capture editor state while the session
        // still owns it; no native calls or composition run inside this short ownership lock.
        if (!_sessionGuard.TryApply(generation, () =>
        {
            if (playbackToken.IsCancellationRequested || _isPausing || _stopRequested || _isDisposing
                || !_isEnabled.Value || Scene is not { } currentScene)
            {
                IsPlaying.Value = false;
                return;
            }

            scene = currentScene;
            BufferStatusViewModel bufferStatus = EditViewModel.BufferStatus;
            frameCacheManager = EditViewModel.FrameCacheManager.Value;
            sampleRate = EditViewModel.Composer.Value.SampleRate;
            rate = GetFrameRate();
            startTime = _editorClock.CurrentTime.Value;
            // Computed once for the producer and the ticker, rounded as the paused preview rounds the playhead.
            range = PlaybackRange.From(startTime, scene.Start, scene.Duration, rate);
            scene.Edited -= OnSceneEdited;
            _currentFrameSubscription?.Dispose();
            _currentFrameSubscription = null;
            bufferStatus.StartTime.Value = startTime;
            bufferStatus.EndTime.Value = startTime;
            frameCacheManager.Options = frameCacheManager.Options with
            {
                DeletionStrategy = FrameCacheDeletionStrategy.BackwardBlock
            };
            frameCacheManager.CurrentFrame = range.StartFrame;
            ready = true;
        }) || !ready)
        {
            return false;
        }

        TimeSpan tick = TimeSpan.FromSeconds(1d / rate);
        PlaybackTicker? ticker = null;
        try
        {
            BufferedPlayer playerImpl = null!;
            if (!_sessionGuard.TryApply(generation, () =>
                    playerImpl = new BufferedPlayer(EditViewModel, IsPlaying, rate, playbackToken)))
                return false;
            using var playerLifetime = playerImpl;
            if (!_sessionGuard.TryApply(generation, () => playerImpl.Start(range)))
                return false;
            _logger.LogInformation("Start the playback. ({SceneId}, {Rate}, {StartFrame}, {EndFrame})",
                _editViewModel.SceneId, rate, range.StartFrame, range.EndFrame);

            var clock = new AudioPlaybackClock();
            Task audioTask = PlayAudio(scene, clock, startTime, sampleRate, generation, playbackToken);

            // 音声バッファの準備に1フレーム以上かかると、映像だけが先に進んでしまい、
            // その後音声がアンカーされた瞬間に「映像が先行した状態」で止まってしまう。
            // 音声側が再生を開始（または音声なしと判明して終了）するまで待ってから
            // ウォールクロックの基準点を取得する。
            await clock.StartedTask;

            ticker = new PlaybackTicker(
                this, generation, playerImpl, clock, rate, tick, range.StartFrame, range.EndFrame, startTime,
                DateTime.UtcNow);

            await using var timer = new Timer(ticker.OnTick, null, tick, tick);

            await Task.WhenAll(ticker.Completion, audioTask);

            // Committing the recorded cache blocks is a shared write, so gate it on ownership like the
            // finally/rewind paths below: a task disowned by a Pause() timeout that unblocks after a
            // newer session started must not stomp that session's frame-cache bookkeeping.
            _sessionGuard.TryApply(generation, frameCacheManager.UpdateBlocks);
        }
        finally
        {
            // Restore the stopped state only while this task owns the session; otherwise the new owner or
            // Pause()'s timeout path does. On the UI thread, after retiring the ticker, so nothing it posted
            // lands after the stopped state, the rewind or a restart.
            PlaybackTicker? finishedTicker = ticker;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                finishedTicker?.Retire();
                TryApplyLogged(generation, RestoreStoppedPreviewState);
            }, Avalonia.Threading.DispatcherPriority.Normal);

            _logger.LogInformation("End the playback. ({SceneId})", _editViewModel.SceneId);
        }

        // ループ再生が自然に終端へ達したら先頭に戻して再開を要求する。Pause() のタイムアウトで所有を
        // 失ったタスクは、停止したエディタや後のセッションの再生位置を戻さない。
        if (_sessionGuard.Owns(generation) && IsLoopEnabled.Value && ticker is { ReachedNaturalEnd: true }
            && Scene is { } loopScene)
        {
            return await TryApplyOnUIThreadAsync(generation, () => _editorClock.Seek(loopScene.Start));
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
            try
            {
                IsPlaying.Value = false;
            }
            finally
            {
                // Runs even when an IsPlaying subscriber throws, so the preview follows the playhead and edits again.
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
                ReattachSceneEdited();
            }
        }
    }

    // Remove-then-add so the handler stays subscribed exactly once.
    private void ReattachSceneEdited()
    {
        if (Scene != null)
        {
            Scene.Edited -= OnSceneEdited;
            Scene.Edited += OnSceneEdited;
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

    public void ToggleLoop()
    {
        IsLoopEnabled.Value = !IsLoopEnabled.Value;
    }

    public Task Pause()
    {
        lock (_renderRequestLock)
        {
            // Publish one complete drain before it can start, even for concurrent or reentrant
            // callers. UI dispatch also keeps its completion serialized with history mutations.
            if (!_pauseTask.IsCompleted)
                return _pauseTask;
            _isPausing = true;
            return _pauseTask = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                PauseCore, Avalonia.Threading.DispatcherPriority.Normal);
        }
    }

    private async Task PauseCore()
    {
        CancellationTokenSource? renderCts;
        lock (_renderRequestLock)
        {
            renderCts = _cts;
            _cts = null;
        }

        try
        {
            _stopRequested = true;
            _isShuttling = false;
            // Retire before waiting, so late/queued callbacks cannot enter behind a drain barrier.
            _sessionGuard.Disown();
            renderCts?.Cancel();
            if (IsPlaying.Value)
                _logger.LogInformation("Pause the playback. ({SceneId})", _editViewModel.SceneId);
            IsPlaying.Value = false;
            PlaybackSpeed.Value = 1.0f;
            PlaybackDirection.Value = ViewModels.PlaybackDirection.Stopped;

            Task playbackTask = _playbackTask;
            try
            {
                await WaitForPlaybackStopAsync(playbackTask, s_pauseTimeout, _logger, _editViewModel.SceneId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Playback task faulted before pause. ({SceneId})", _editViewModel.SceneId);
            }
            _playbackTask = Task.CompletedTask;

            // Native device work runs outside these dispatchers and may be abandoned. Scene work
            // cannot: a slow Compose/EvaluateGraphics must finish before history or disposal can
            // mutate its inputs. Awaiting these barriers keeps the UI dispatcher responsive.
            await Task.WhenAll(
                ComposeThread.Dispatcher.InvokeAsync(static () => { }),
                RenderThread.Dispatcher.InvokeAsync(static () => { }));
            RestoreStoppedPreviewState();
        }
        finally
        {
            renderCts?.Dispose();
            lock (_renderRequestLock)
            {
                _isPausing = false;
            }
        }

        // Run after the caller's synchronous history mutation. During the drain, preview handoffs
        // were suppressed so they could not enqueue scene work behind the barriers.
        if (!_isDisposing)
            Avalonia.Threading.Dispatcher.UIThread.Post(QueueRender, Avalonia.Threading.DispatcherPriority.Background);
    }

    // Waits at most timeout: true when the playback task finished (its fault is rethrown), false when a task
    // blocked in a native audio call is left running, logged and observed, so it cannot hold the history gate.
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
            "Playback task did not stop within {Timeout} on pause; abandoning native playback and draining scene work. ({SceneId})",
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

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing PlayerViewModel. ({SceneId})", _editViewModel.SceneId);
        CancellationTokenSource? renderCts;
        lock (_renderRequestLock)
        {
            _isDisposing = true;
            renderCts = _cts;
            _cts = null;
        }

        // Cancel preview renders before Pause() can yield, under the lock QueueRender takes, so a new source is
        // never disposed under the request holding its token. Pause then drains the compose and render
        // dispatchers, so the render thread cannot call OnNext on the subjects disposed below.
        renderCts?.Cancel();
        await Pause();
        renderCts?.Dispose();
        Scene!.Edited -= OnSceneEdited;
        _disposables.Dispose();
        _currentFrameSubscription?.Dispose();
        AfterRendered.Dispose();
        // Complete so an abandoned audio task can finish without throwing.
        _audioFramePushed.OnCompleted();
        BeginEditTimecodeRequested.Dispose();
        PreviewInvalidated = null;
        Scene = null!;
        PreviewImage.Value?.Dispose();
        PreviewImage.Value = null!;
        _logger.LogInformation("Disposed PlayerViewModel. ({SceneId})", _editViewModel.SceneId);
    }
}
