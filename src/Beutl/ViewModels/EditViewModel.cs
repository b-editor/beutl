using System.Reactive.Disposables;
using System.Reactive.Linq;
using Beutl.Audio;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.Components.AiWorkspaceTab.ViewModels;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Observers;
using Beutl.Editor.Services.AI;
using Beutl.Graphics.Rendering;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.Tools;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels;

public sealed partial class EditViewModel
    : ISavableEditorContext, IUndoRedoEditorContext, IAiJobResultEditorContext, ISupportAutoSaveEditorContext,
        IPreviewRenderQuality, IFrameCacheInvalidator, IHistoryNavigator
{
    private readonly ILogger _logger = Log.CreateLogger<EditViewModel>();
    private readonly AutoSaveService _autoSaveService = new();
    private readonly CancellationTokenSource _autoSaveCancellation = new();

    // Auto save cannot be turned off by users; tests suppress it to hold edits in memory.
    internal static bool IsAutoSaveSuppressedForTesting { get; set; }
    private readonly HistoryMutationPlaybackGuard _historyMutationPlaybackGuard = new();

    private readonly CompositeDisposable _disposables = [];
    private readonly TimelineOptionsProviderImpl _timelineOptionsProvider;
    private readonly EditorClockImpl _editorClock;
    private readonly EditorSelectionImpl _editorSelection;
    private readonly ElementAdderImpl _elementAdder;
    private ElementNudgeService? _elementNudgeService;
    private readonly IClipboardGateway _clipboardGateway = new Beutl.Editor.Components.Services.AvaloniaClipboardGateway();
    private volatile bool _viewStateSaveSuppressed;
    private readonly HashSet<string> _pendingProxyInvalidations = new(StringComparer.Ordinal);
    private bool _proxyInvalidationScheduled;
    private volatile bool _disposed;

    internal bool IsDisposingOrDisposed => _disposed;

    public EditViewModel(Scene scene, Beutl.Api.Services.ExtensionProvider extensionProvider, EditorService editorService)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(extensionProvider);
        ArgumentNullException.ThrowIfNull(editorService);

        _logger.LogInformation("Initializing EditViewModel for Scene ({SceneId}).", scene.Id.Value);

        _services = CreateServices();
        Scene = scene;
        ExtensionProvider = extensionProvider;
        EditorService = editorService;
        SceneId = scene.Id.Value.ToString();

        _timelineOptionsProvider = new TimelineOptionsProviderImpl(scene)
            .DisposeWith(_disposables);
        _editorClock = new EditorClockImpl(scene)
            .DisposeWith(_disposables);
        _editorSelection = new EditorSelectionImpl()
            .DisposeWith(_disposables);

        PreviewScale = new ReactivePropertySlim<RenderScale>(RenderScale.Full)
            .DisposeWith(_disposables);

        // On-screen previewer size (physical px), used by RenderScale.FitToPreviewer.
        PreviewSurfaceSize = new ReactivePropertySlim<Beutl.Graphics.Size>(default)
            .DisposeWith(_disposables);

        IObservable<(PixelSize FrameSize, float OutputScale)> frameSizeAndScale =
            scene.FrameSize.GetObservable()
                .CombineLatest(PreviewScale, PreviewSurfaceSize,
                    (frameSize, scale, surface) => (FrameSize: frameSize, OutputScale: scale.ResolveOutputScale(frameSize, surface)))
                .DistinctUntilChanged();

        Renderer = frameSizeAndScale
            .Select(t => new SceneRenderer(Scene, RenderIntent.Preview, t.OutputScale, maxWorkingScale: WorkingScaleCeiling.Preview(t.OutputScale)))
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;
        Project? project = Scene.FindHierarchicalParent<Project>();
        var channelLayout = new ReactivePropertySlim<AudioChannelLayout>(project.GetChannelLayout())
            .DisposeWith(_disposables);
        Composer = scene.FrameSize.GetObservable().CombineLatest(channelLayout)
            .Select(t => new SceneComposer(Scene)
            {
                SampleRate = project.GetSampleRate(),
                ChannelLayout = t.Second
            })
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;

        // Derived from Renderer so the swap is ordered: cache subscribers see the new Renderer.
        FrameCacheManager = Renderer
            .Select(r => new FrameCacheManager(r.FrameSize, CreateFrameCacheOptions(), CreateFrameCacheMaxSize())
            {
                IsEnabled = config.IsFrameCacheEnabled.Value
            })
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        Player = new PlayerViewModel(this);
        project?.ChannelLayout.GetObservable().Skip(1)
            .Select(layout => Observable.FromAsync(async () =>
            {
                // Retire the device queue before disposing the composer it is reading.
                await Player.Pause();
                return layout.ToAudioChannelLayout();
            }))
            .Switch()
            .ObserveOnUIDispatcher()
            .Subscribe(layout => channelLayout.Value = layout, ex => _logger.LogError(ex, "Could not change audio layout."))
            .DisposeWith(_disposables);
        GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode.GetObservable()
            .Skip(1)
            .Subscribe(_ =>
            {
                FrameCacheManager.Value.Clear();
                // Clearing the cache alone leaves a paused viewport showing the old decode path until
                // an unrelated edit/scrub; queue a render so the switch is visible immediately.
                Player.QueuePreviewRender();
            })
            .DisposeWith(_disposables);
        GlobalConfiguration.Instance.ProxyStoreConfig.DefaultPreset.GetObservable()
            .Skip(1)
            .Subscribe(_ =>
            {
                FrameCacheManager.Value.Clear();
                Player.QueuePreviewRender();
            })
            .DisposeWith(_disposables);

        // Subscribe through the swap-stable facade so a store-root change keeps delivering Changed events
        // (the facade forwards them to the rebuilt store) instead of leaving this bound to the old one.
        if (ProxyMediaServices.Current?.StoreFacade is { } proxyStore)
        {
            proxyStore.Changed += OnProxyStoreChanged;
            Disposable.Create(() => proxyStore.Changed -= OnProxyStoreChanged)
                .DisposeWith(_disposables);
        }

        config.PropertyChanged += OnEditorConfigPropertyChanged;

        HookCommandStateNotifier();
        var sequenceGenerator = new OperationSequenceGenerator();
        var observer = new CoreObjectOperationObserver(null, Scene, sequenceGenerator)
            .DisposeWith(_disposables);
        HistoryManager = new HistoryManager(sequenceGenerator);
        HistoryManager.Subscribe(observer)
            .DisposeWith(_disposables);
        WarnAboutEditsPendingAfterInput();
        Usage = new EditorUsageTracker(Scene, HistoryManager).DisposeWith(_disposables);

        observer.Operations
            .Buffer(HistoryManager.StateChanged)
            .Subscribe(OnChangeOperations)
            .DisposeWith(_disposables);

        BufferStatus = new BufferStatusViewModel(this)
            .DisposeWith(_disposables);

        DockHost = new DockHostViewModel(this);
        _editorSelection.SelectedObject
            .Subscribe(OpenSelectionInPropertyTabs)
            .DisposeWith(_disposables);

        _elementAdder = new ElementAdderImpl(this);

        _autoSaveService.SaveError
            .Subscribe(_ =>
            {
                _autoSaveFailed = true;
                NotificationService.ShowError(string.Empty, MessageStrings.FileSaveException);
            })
            .DisposeWith(_disposables);
        _autoSaveService.DisposeWith(_disposables);

        RestoreState();
        CaptureSavedMediaUris();
        ScheduleMediaFingerprints();
        NotifyMissingMedia();
        NotifyRecoveryProtectedElements();

        _logger.LogInformation("Initialized EditViewModel for Scene ({SceneId}).", SceneId);
    }

    // Elements whose file recovery keeps are read-only. Say which ones, once, when the scene opens;
    // the notice stays until it is closed or this editor closes.
    private void NotifyRecoveryProtectedElements()
    {
        if (Beutl.Editor.Services.RecoveryProtectionNotice.Create(Scene, _autoSaveCancellation.Token) is { } notice)
        {
            NotificationService.Show(notice);
        }
    }

    private static IObservable<long> CreateFrameCacheMaxSize()
    {
        return GlobalConfiguration.Instance.EditorConfig
            .FrameCacheMaxSize.GetObservable()
            .Select(v => (long)(v * 1024 * 1024));
    }

    private static FrameCacheOptions CreateFrameCacheOptions()
    {
        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
        return new FrameCacheOptions(Scale: (FrameCacheScale)config.FrameCacheScale.Value,
            ColorType: (FrameCacheColorType)config.FrameCacheColorType.Value);
    }

    // Telemetryで使う
    public string SceneId { get; }

    public Scene Scene { get; private set; }

    Scene IAiJobResultEditorContext.Scene => Scene;

    MediaTime IAiJobResultEditorContext.CurrentTime => Player.CurrentFrame.Value;

    IElementAdder IAiJobResultEditorContext.ElementAdder => _elementAdder;

    int IAiJobResultEditorContext.GetNextLayer(MediaTime start)
    {
        return Scene.Children
            .Where(item => item.Start.Value <= start && start < item.Range.End)
            .Select(item => item.ZIndex.Value)
            .DefaultIfEmpty(-1)
            .Max() + 1;
    }

    // Host services injected from the composition root via EditorExtension.TryCreateContext;
    // exposed so editor-scoped view models (DockHost, output, property editors) can reach them.
    public Beutl.Api.Services.ExtensionProvider ExtensionProvider { get; }

    public EditorService EditorService { get; }

    public ReadOnlyReactivePropertySlim<SceneRenderer> Renderer { get; }

    /// <summary>Per-edit-view preview render quality. Non-persisted; rebuilds Renderer and FrameCacheManager.</summary>
    public ReactivePropertySlim<RenderScale> PreviewScale { get; }

    /// <summary>Selectable preview-quality options for the preview-scale picker.</summary>
    public RenderScale[] PreviewScaleOptions { get; } = Enum.GetValues<RenderScale>();

    IReactiveProperty<RenderScale> IPreviewRenderQuality.PreviewScale => PreviewScale;

    IReadOnlyList<RenderScale> IPreviewRenderQuality.PreviewScaleOptions => PreviewScaleOptions;

    /// <summary>On-screen previewer surface size in physical pixels, used by FitToPreviewer.</summary>
    public ReactivePropertySlim<Beutl.Graphics.Size> PreviewSurfaceSize { get; }

    public ReadOnlyReactivePropertySlim<SceneComposer> Composer { get; }

    public ReactivePropertySlim<bool> IsEnabled { get; } = new(true);

    public PlayerViewModel Player { get; private set; }

    public BufferStatusViewModel BufferStatus { get; private set; }

    public HistoryManager HistoryManager { get; private set; }

    internal EditorUsageTracker? Usage { get; }

    public ReadOnlyReactivePropertySlim<FrameCacheManager> FrameCacheManager { get; private set; }

    public EditorExtension Extension => SceneEditorExtension.Instance;

    public CoreObject Object => Scene;

    IReactiveProperty<bool> IEditorContext.IsEnabled => IsEnabled;

    public DockHostViewModel DockHost { get; }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing EditViewModel ({SceneId}).", SceneId);
        Scene scene = Scene;
        Exception? firstFailure = null;

        void RecordFailure(Exception error)
        {
            if (Interlocked.CompareExchange(ref firstFailure, error, null) is not null)
            {
                try { _logger.LogWarning(error, "An additional editor cleanup step failed ({SceneId}).", SceneId); }
                catch { } // Preserve the original disposal failure even if its diagnostic cannot be written.
            }
        }

        void Cleanup(Action action)
        {
            try { action(); }
            catch (Exception error) { RecordFailure(error); }
        }

        async ValueTask CleanupAsync(Func<ValueTask> action)
        {
            try { await action(); }
            catch (Exception error) { RecordFailure(error); }
        }

        // Block any proxy-invalidation flush already posted to the UI thread from running after this
        // nulls Scene / disposes FrameCacheManager below.
        _disposed = true;
        Cleanup(_autoSaveCancellation.Cancel);
        Cleanup(DismissMissingMediaNotification);
        Cleanup(HasMediaRepairs.Dispose);
        GlobalConfiguration.Instance.EditorConfig.PropertyChanged -= OnEditorConfigPropertyChanged;
        try
        {
            if (scene.Uri is not null && !EditorService.IsWorktreeMutationActive)
            {
                using IDisposable fileWrite = await EditorService.BeginProjectFileWriteAsync(
                    CancellationToken.None);
                SaveState();
            }
            else if (scene.Uri is not null)
            {
                _logger.LogDebug(
                    "Skipping the final view-state save during a worktree mutation ({SceneId}).",
                    SceneId);
            }
        }
        catch (Exception error)
        {
            RecordFailure(error);
        }
        finally
        {
            // A view-state save failure must not strand a retired editor's resources. Attempt
            // every cleanup step, and report the original error only after ownership is released.
            Cleanup(() => _editorSelection.Select(null));
            // Player を破棄する前にイベント購読を外し、Subject 破棄後の OnNext を抑止する。
            Cleanup(DisposeCommandStateNotifier);
            // Tool contexts can own paid-AI operations that still publish through the editor.
            // Cancel and drain them before retiring element handlers and the player.
            AiWorkspaceViewModel[] aiWorkspaces = [];
            Cleanup(() => aiWorkspaces = DockHost.Factory.EnumerateTools()
                .Select(static tool => tool.ToolContext)
                .OfType<AiWorkspaceViewModel>()
                .ToArray());
            Cleanup(DockHost.Dispose);
            await Task.WhenAll(aiWorkspaces.Select(workspace => CleanupAsync(workspace.DisposeAsync).AsTask()));
            // Retire and cancel UI-initiated element operations before waiting for handler leases.
            // Awaiting yields the UI thread so cancellation continuations can release those leases.
            await CleanupAsync(_elementAdder.DisposeAsync);
            await CleanupAsync(() => Player.DisposeAsync());
            Cleanup(_services.Dispose);
            Cleanup(_historyMutationPlaybackGuard.Dispose);
            Cleanup(_disposables.Dispose);
            Cleanup(IsEnabled.Dispose);
            Player = null!;
            BufferStatus = null!;

            // Closing the editor discards every live and redoable owner of unsaved sidecars and AI
            // resources, so its scene-scoped temporary directory must not survive the tab.
            Cleanup(() => UnsavedSceneStorage.Cleanup(scene.Id.Value));
            Scene = null!;
            Cleanup(HistoryManager.Clear);
            Cleanup(() => FrameCacheManager.Value.Dispose());
            Cleanup(FrameCacheManager.Dispose);
        }

        if (firstFailure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstFailure).Throw();

        _logger.LogInformation("Disposed EditViewModel ({SceneId}).", SceneId);
    }

    public T? FindToolTab<T>(Func<T, bool> condition)
        where T : IToolContext
    {
        return DockHost.FindToolTab(condition);
    }

    public T? FindToolTab<T>()
        where T : IToolContext
    {
        return FindToolTab<T>(_ => true);
    }

    public bool OpenToolTab(IToolContext item)
    {
        return DockHost.OpenToolTab(item);
    }

    public void CloseToolTab(IToolContext item)
    {
        DockHost.CloseToolTab(item);
    }

    public ValueTask<bool> UndoAsync()
    {
        // During a drag, Undo cancels only the drag, as Escape does.
        if (HistoryManager.TryCancelGesture())
            return ValueTask.FromResult(true);

        return ExecuteHistoryMutationAsync(
            "Undo",
            "Undoing last command.",
            "Undo completed.",
            // A pending transaction is flushed onto the undo stack by BeforeMutation
            // before Undo() runs, so it can revert scene state even when CanUndo is false.
            () => HistoryManager.CanUndo || HistoryManager.HasPendingOperations,
            HistoryManager.Undo);
    }

    public ValueTask<bool> RedoAsync()
    {
        return ExecuteHistoryMutationAsync(
            "Redo",
            "Redoing last undone command.",
            "Redo completed.",
            // Redo() rolls back a pending transaction before checking the redo stack,
            // so it can revert scene state even when CanRedo is false.
            () => HistoryManager.CanRedo || HistoryManager.HasPendingOperations,
            HistoryManager.Redo);
    }

    internal ValueTask<bool> JumpToHistoryAsync(int index)
    {
        return ExecuteHistoryMutationAsync(
            $"JumpTo({index})",
            null,
            null,
            // A blocked jump still drains pending edits, so pause before that commit.
            () => HistoryManager.HasPendingOperations || HistoryManager.WouldJumpToMove(index),
            () => HistoryManager.JumpTo(index));
    }

    // Resolves the entry inside the guarded change: flushing pending edits first can shift or drop entries.
    // A cancellation (the user switched editors) leaves this history alone. One that arrives while playback
    // pauses still lets the guard drain pending edits, but only while paused: whether to pause does not
    // depend on the token, since a drain without the pause would race a live player.
    internal ValueTask<bool> JumpToHistoryAsync(HistoryEntry entry, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return ValueTask.FromResult(false);

        int IndexOf() => Array.IndexOf(HistoryManager.GetEntriesSnapshot(), entry);
        return ExecuteHistoryMutationAsync(
            "JumpTo",
            null,
            null,
            () => HistoryManager.HasPendingOperations
                  || IndexOf() is var index and >= 0 && HistoryManager.WouldJumpToMove(index),
            () => !cancellationToken.IsCancellationRequested
                  && IndexOf() is var index and >= 0 && HistoryManager.JumpTo(index));
    }

    ValueTask<bool> IHistoryNavigator.JumpToAsync(int index) => JumpToHistoryAsync(index);

    ValueTask<bool> IHistoryNavigator.JumpToAsync(HistoryEntry entry, CancellationToken cancellationToken)
        => JumpToHistoryAsync(entry, cancellationToken);

    internal ValueTask<TResult> ExecuteGuardedHistoryMutationAsync<TResult>(
        Func<bool> shouldPause,
        Func<TResult> mutate,
        CancellationToken cancellationToken = default)
    {
        return _historyMutationPlaybackGuard.RunAsync(
            Player,
            () =>
            {
                // Closing may have started while Pause awaited the compose/render barriers.
                ObjectDisposedException.ThrowIf(_disposed, this);
                HistoryManager.FlushPendingMutations();
            },
            shouldPause,
            mutate,
            cancellationToken);
    }

    private async ValueTask<bool> ExecuteHistoryMutationAsync(
        string operationName,
        string? startMessage,
        string? completedMessage,
        Func<bool> shouldPause,
        Func<bool> mutate)
    {
        UsageTelemetry? usage = UsageTelemetry.Current;
        long epoch = 0;
        bool collect = usage?.TryGetCollectionEpoch(out epoch) == true;
        string tool = Usage?.ActiveTool ?? "Editor";
        try
        {
            if (startMessage is not null)
            {
                _logger.LogInformation("{Message}", startMessage);
            }

            bool changed = await ExecuteGuardedHistoryMutationAsync(shouldPause, mutate);
            if (changed && collect)
                usage!.Record("editor.history", tool,
                    operationName is "Undo" or "Redo" ? operationName : "JumpTo", epoch: epoch);
            if (changed && completedMessage is not null)
            {
                _logger.LogInformation("{Message}", completedMessage);
            }

            return changed;
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "{OperationName} skipped because the editor is disposed.", operationName);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{OperationName} failed.", operationName);
            NotificationService.ShowError(Strings.History,
                ex is HistoryReplayException ? Strings.History_RestrictedAfterFailure : Strings.History_OperationFailed);
            return false;
        }
    }

    public async ValueTask<bool> SaveAsync()
    {
        // A direct explicit save (including media repair) must not race an older auto-save's
        // background replacements. Lifecycle callers may already hold the project write lease.
        // A queued auto-save can replace the task while this waits, so wait until it settles.
        Task pending;
        do
        {
            pending = _autoSaveTask;
            try { await pending; }
            catch (Exception ex) when (!_disposed)
            {
                // The auto-save reported its own failure; the explicit save still runs.
                _logger.LogDebug(ex, "An earlier auto-save did not complete.");
            }
        }
        while (!ReferenceEquals(pending, _autoSaveTask));
        using UsageTelemetry.Operation? usage = UsageTelemetry.Current?.Begin("scene.save");
        Scene scene = Scene;
        _logger.LogInformation("Saving scene ({SceneId}).", scene.Id.Value);
        Uri sceneUri = scene.Uri
            ?? throw new InvalidOperationException("An unsaved scene needs a destination before it can be saved.");
        UnsavedSceneStorage.SaveRelocation relocation = new(scene, sceneUri);
        // A failure at any step restores the project, scene, element and resource files this
        // save replaced, instead of leaving the files written before it on disk.
        using (StorageWriteTransaction transaction = StorageWriteTransaction.Begin())
        {
            try
            {
                relocation.Apply();
                Parallel.ForEach(scene.Children, item => CoreSerializer.StoreToUri(item, item.Uri!));
                // The scene is stored without SaveReferencedObjects, which would store the children
                // again, so its fingerprint file is saved, or removed, here too.
                scene.SaveMediaFingerprintFile();
                // The scene is the commit record for every child/resource URI. Persist it only
                // after every referenced file is durable at its new location.
                CoreSerializer.StoreToUri(scene, sceneUri, CoreSerializationMode.Write);
                transaction.Commit();
            }
            catch
            {
                relocation.Rollback();
                transaction.Rollback();
                throw;
            }
        }

        relocation.Commit();
        if (scene.FindHierarchicalParent<Project>() is { } project)
            DuplicateIds.Report(project, "save");

        SaveState(isExplicitUserSave: true);
        _logger.LogInformation("Scene ({SceneId}) saved successfully.", scene.Id.Value);
        usage?.Complete();

        HasMediaRepairs.Value = false;
        CaptureSavedMediaUris();
        ScheduleMediaFingerprints(force: !_fingerprintsFlushedForSave);
        _fingerprintsFlushedForSave = false;
        return true;
    }
}
