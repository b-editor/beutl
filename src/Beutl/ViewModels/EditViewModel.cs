using System.ComponentModel;
using System.Numerics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Security;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Operations;
using Beutl.Editor.Services.AI;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.Dock;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Dispatcher = Avalonia.Threading.Dispatcher;

namespace Beutl.ViewModels;

public sealed partial class EditViewModel : IEditorContext, IEditorContextPublicationGate, IAiJobResultEditorContext, ISupportAutoSaveEditorContext, IPreviewRenderQuality
{
    private readonly ILogger _logger = Log.CreateLogger<EditViewModel>();
    private readonly AutoSaveService _autoSaveService = new();
    private readonly HistoryMutationPlaybackGuard _historyMutationPlaybackGuard = new();

    private readonly CompositeDisposable _disposables = [];
    private readonly TimelineOptionsProviderImpl _timelineOptionsProvider;
    private readonly EditorClockImpl _editorClock;
    private readonly EditorSelectionImpl _editorSelection;
    private readonly ElementAdderImpl _elementAdder;
    private readonly IEditorContextCloseService _contextCloseService;
    private SceneTimeRangeService? _sceneTimeRangeService;
    private ElementResizeService? _elementResizeService;
    private ElementSlipService? _elementSlipService;
    private ElementDuplicateService? _elementDuplicateService;
    private ElementMoveService? _elementMoveService;
    private ElementGapService? _elementGapService;
    private ElementClipboardService? _elementClipboardService;
    private ElementStructureService? _elementStructureService;
    private ElementAttributeService? _elementAttributeService;
    private ElementNudgeService? _elementNudgeService;
    private LayerMoveService? _layerMoveService;
    private LayerAttributeService? _layerAttributeService;
    private SceneSettingsService? _sceneSettingsService;
    private KeyFrameClipboardService? _keyFrameClipboardService;
    private NodeGraphMutationService? _nodeGraphMutationService;
    private ElementObjectService? _elementObjectService;
    private IClipboardGateway? _clipboardGateway;
    private Services.Adapters.PropertyEditorFactoryAdapter? _propertyEditorFactory;
    private Services.Adapters.PropertiesEditorFactoryImpl? _propertiesEditorFactory;
    private volatile bool _viewStateSaveSuppressed;
    private readonly HashSet<string> _pendingProxyInvalidations = new(StringComparer.Ordinal);
    private bool _proxyInvalidationScheduled;
    private volatile bool _disposed;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;
    private TaskCompletionSource? _publicationDrain;
    private int _activePublications;
    private readonly Task _restoreTask;
    private readonly TaskCompletionSource _constructionCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public EditViewModel(
        Scene scene,
        Beutl.Api.Services.ExtensionProvider extensionProvider,
        EditorService editorService,
        IEditorContextCloseService closeService)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(extensionProvider);
        ArgumentNullException.ThrowIfNull(editorService);
        ArgumentNullException.ThrowIfNull(closeService);

        _logger.LogInformation("Initializing EditViewModel for Scene ({SceneId}).", scene.Id);

        Scene = scene;
        ExtensionProvider = extensionProvider;
        EditorService = editorService;
        _contextCloseService = new BoundContextCloseService(this, closeService);
        SceneId = scene.Id.ToString();

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

        // Rebuild only when the resolved output scale actually changes (DistinctUntilChanged).
        IObservable<(PixelSize FrameSize, float OutputScale)> frameSizeAndScale =
            scene.GetObservable(Scene.FrameSizeProperty)
                .CombineLatest(PreviewScale, PreviewSurfaceSize,
                    (frameSize, scale, surface) => (FrameSize: frameSize, OutputScale: scale.ResolveOutputScale(frameSize, surface)))
                .DistinctUntilChanged();

        Renderer = frameSizeAndScale
            .Select(t => new SceneRenderer(Scene, t.OutputScale, maxWorkingScale: WorkingScaleCeiling.Preview(t.OutputScale)))
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;
        // SceneComposer is scale-independent; rebuild only on frame-size changes.
        Composer = scene.GetObservable(Scene.FrameSizeProperty)
            .Select(_ => new SceneComposer(Scene))
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;

        // Derived from Renderer so the swap is ordered: cache subscribers see the new Renderer.
        FrameCacheManager = Renderer
            .Select(r => new FrameCacheManager(r.FrameSize, CreateFrameCacheOptions(), CreateFrameCacheMaxSize())
            {
                IsEnabled = config.IsFrameCacheEnabled
            })
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        Player = new PlayerViewModel(this);
        GlobalConfiguration.Instance.EditorConfig.GetObservable(EditorConfig.PreviewSourceModeProperty)
            .Skip(1)
            .Subscribe(_ =>
            {
                FrameCacheManager.Value.Clear();
                // Clearing the cache alone leaves a paused viewport showing the old decode path until
                // an unrelated edit/scrub; queue a render so the switch is visible immediately.
                Player.QueuePreviewRender();
            })
            .DisposeWith(_disposables);
        GlobalConfiguration.Instance.ProxyStoreConfig.GetObservable(ProxyStoreConfig.DefaultPresetProperty)
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
        Commands = new KnownCommandsImpl(scene, this);
        var sequenceGenerator = new OperationSequenceGenerator();
        var observer = new CoreObjectOperationObserver(null, Scene, sequenceGenerator)
            .DisposeWith(_disposables);
        HistoryManager = new HistoryManager(Scene, sequenceGenerator);
        HistoryManager.Subscribe(observer)
            .DisposeWith(_disposables);

        observer.Operations
            .Buffer(HistoryManager.StateChanged)
            .Subscribe(OnChangeOperations)
            .DisposeWith(_disposables);

        BufferStatus = new BufferStatusViewModel(this)
            .DisposeWith(_disposables);

        DockHost = new DockHostViewModel(SceneId, this);

        _elementAdder = new ElementAdderImpl(this);
        _clipboardGateway = new Beutl.Editor.Components.Services.AvaloniaClipboardGateway();

        _autoSaveService.SaveError
            .Subscribe(_ =>
                NotificationService.ShowError(string.Empty, MessageStrings.FileSaveException))
            .DisposeWith(_disposables);
        _autoSaveService.DisposeWith(_disposables);

        _restoreTask = RestoreStateAsync();
        _constructionCompleted.TrySetResult();

        _logger.LogInformation("Initialized EditViewModel for Scene ({SceneId}).", SceneId);
    }

    private static IObservable<long> CreateFrameCacheMaxSize()
    {
        return GlobalConfiguration.Instance.EditorConfig
            .GetObservable(EditorConfig.FrameCacheMaxSizeProperty)
            .Select(v => (long)(v * 1024 * 1024));
    }

    private static FrameCacheOptions CreateFrameCacheOptions()
    {
        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
        return new FrameCacheOptions(Scale: (FrameCacheScale)config.FrameCacheScale,
            ColorType: (FrameCacheColorType)config.FrameCacheColorType);
    }

    private void OnEditorConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is EditorConfig config)
        {
            if (e.PropertyName is nameof(EditorConfig.FrameCacheColorType) or nameof(EditorConfig.FrameCacheScale))
            {
                _logger.LogInformation("Updating FrameCacheManager options due to EditorConfig change.");
                FrameCacheManager.Value.Options = FrameCacheManager.Value.Options with
                {
                    ColorType = (FrameCacheColorType)config.FrameCacheColorType,
                    Scale = (FrameCacheScale)config.FrameCacheScale
                };
            }
            else if (e.PropertyName is nameof(EditorConfig.IsFrameCacheEnabled))
            {
                _logger.LogInformation("Updating FrameCacheManager IsEnabled due to EditorConfig change.");
                FrameCacheManager.Value.IsEnabled = config.IsFrameCacheEnabled;
                if (!config.IsFrameCacheEnabled)
                {
                    FrameCacheManager.Value.Clear();
                }
            }
            else if (e.PropertyName is nameof(EditorConfig.IsNodeCacheEnabled)
                     or nameof(EditorConfig.NodeCacheMaxPixels)
                     or nameof(EditorConfig.NodeCacheMinPixels))
            {
                _logger.LogInformation("Updating RenderNodeCacheHelper options due to EditorConfig change.");
                Renderer.Value.CacheOptions = RenderCacheOptions.CreateFromGlobalConfiguration();
            }
        }
    }

    private void OnProxyStoreChanged(object? sender, ProxyStoreChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        // A store swap replaces every entry, and e.Source is not meaningful — clear the whole frame
        // cache and re-render rather than invalidating a single source's ranges.
        if (e.Kind == ProxyStoreChangeKind.Reset)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed || FrameCacheManager.Value.IsDisposed)
                    return;

                FrameCacheManager.Value.Clear();
                Player.QueuePreviewRender();
            });
            return;
        }

        if (e.Kind is not (ProxyStoreChangeKind.Registered
            or ProxyStoreChangeKind.StateChanged
            or ProxyStoreChangeKind.Deleted))
        {
            return;
        }

        // e.Source.AbsolutePath is already the fingerprint's normalized path.
        bool schedule;
        lock (_pendingProxyInvalidations)
        {
            _pendingProxyInvalidations.Add(e.Source.AbsolutePath);
            schedule = !_proxyInvalidationScheduled;
            _proxyInvalidationScheduled = true;
        }

        // A bulk generate (FR-008 / US2 AC4) fires one store event per proxy state change.
        // Coalesce the burst into a single UI-thread scene walk per tick instead of one
        // walk per event, so the timeline stays smooth while proxies are generated.
        if (schedule)
        {
            Dispatcher.UIThread.Post(FlushPendingProxyInvalidations);
        }
    }

    private void FlushPendingProxyInvalidations()
    {
        HashSet<string> changedSources;
        lock (_pendingProxyInvalidations)
        {
            _proxyInvalidationScheduled = false;
            // Disposal may have run between the Post and this callback; Scene is nulled and the frame
            // cache disposed by then, so drop the pending work and bail rather than touch them.
            if (_disposed || _pendingProxyInvalidations.Count == 0)
            {
                _pendingProxyInvalidations.Clear();
                return;
            }

            changedSources = new HashSet<string>(_pendingProxyInvalidations, StringComparer.Ordinal);
            _pendingProxyInvalidations.Clear();
        }

        // Invalidate only frames of clips that use a changed source, not the whole
        // timeline cache (FR-023; unrelated clips stay editable during a bulk generate).
        // FrameCacheManager only invalidates by frame range, so each changed source is
        // mapped to the ranges of the elements that reference it.
        FrameCacheManager cache = FrameCacheManager.Value;
        if (cache.IsDisposed)
        {
            return;
        }

        List<TimeRange> affectedRanges = [];
        foreach (Element element in Scene.Children)
        {
            if (ElementUsesAnySource(element, changedSources))
            {
                affectedRanges.Add(element.Range);
            }
        }

        if (affectedRanges.Count == 0)
        {
            return;
        }

        int rate = Player.GetFrameRate();
        cache.DeleteAndUpdateBlocks(affectedRanges
            .Select(range => (Start: (int)range.Start.ToFrameNumber(rate),
                End: (int)Math.Ceiling(range.End.ToFrameNumber(rate)))));

        // While paused, the shown bitmap is cloned into PlayerViewModel and does not observe the
        // deletion above, so re-render when the playhead sits in a changed range.
        TimeSpan playhead = Player.CurrentFrame.Value;
        foreach (TimeRange range in affectedRanges)
        {
            if (range.Start <= playhead && playhead < range.End)
            {
                Player.QueuePreviewRender();
                break;
            }
        }
    }

    private static bool ElementUsesAnySource(Element element, IReadOnlySet<string> changedSources)
    {
        // Cover every proxy-aware holder (SourceVideo, VideoSourceNode graph inputs, referenced
        // scenes, and their animated values) so cached frames of a graph/referenced-scene clip are
        // invalidated too, not just those of a top-level SourceVideo's current value.
        foreach (VideoSource source in ProxySourceEnumerator.EnumerateVideoSources(element))
        {
            if (source is not { HasUri: true } || source.Uri is not { IsFile: true } uri)
            {
                continue;
            }

            // Resolve the element's path the same way store change events are keyed (symlink target
            // resolved before folding), so a source referenced via a symlink still matches.
            string elementPath = ProxyFingerprint.ResolveComparableKey(uri.LocalPath);
            if (changedSources.Contains(elementPath))
            {
                return true;
            }
        }

        return false;
    }

    private void OnChangeOperations(IList<ChangeOperation> list)
    {
        if (list.Count == 0)
        {
            return;
        }

        // 影響を受けるタイムレンジを取得
        List<TimeRange> affectedRanges = GetAffectedTimeRanges(list);

        // フレームキャッシュを更新
        if (affectedRanges.Count > 0)
        {
            Task.Run(() =>
            {
                int rate = Player.GetFrameRate();
                FrameCacheManager.Value.DeleteAndUpdateBlocks(affectedRanges
                    .Select(item => (Start: (int)item.Start.ToFrameNumber(rate),
                        End: (int)Math.Ceiling(item.End.ToFrameNumber(rate)))));
            });
        }

        // 自動保存
        if (GlobalConfiguration.Instance.EditorConfig.IsAutoSaveEnabled)
        {
            AutoSave(list);
        }
    }

    private void AutoSave(IList<ChangeOperation> list)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _autoSaveService.AutoSave(list);

            // ビューステートを保存
            try
            {
                SaveState();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An exception occurred while saving the view state.");
            }
        });
    }

    private List<TimeRange> GetAffectedTimeRanges(IList<ChangeOperation> list)
    {
        return [.. list.SelectMany(GetAffectedTimeRangesFromOperation).Where(range => !range.IsEmpty)];
    }

    private IEnumerable<TimeRange> GetAffectedTimeRangesFromOperation(ChangeOperation operation)
    {
        // IUpdatePropertyValueOperationの場合
        if (operation is IUpdatePropertyValueOperation updateOp)
        {
            TimeRange? range = GetAffectedTimeRangeFromUpdateOperation(updateOp);
            if (range.HasValue)
            {
                yield return range.Value;
            }

            yield break;
        }

        // ICollectionChangeOperationの場合
        if (operation is ICollectionChangeOperation collectionOp)
        {
            // Objectプロパティから影響を受けるElementを探す
            Element? element = FindElementFromObject(collectionOp.Object);
            if (element != null)
            {
                yield return element.Range;
            }

            // Itemsから複数のElementを探す
            foreach (Element? item in collectionOp.Items
                         .Select(i => i is CoreObject coreObj ? FindElementFromObject(coreObj) : null)
                         .Where(i => i != null))
            {
                yield return item!.Range;
            }
        }
    }

    private TimeRange? GetAffectedTimeRangeFromUpdateOperation(IUpdatePropertyValueOperation updateOp)
    {
        CoreObject obj = updateOp.Object;
        string propertyPath = updateOp.PropertyPath;

        // ElementのStartまたはLengthプロパティの変更の場合
        if (obj is Element element)
        {
            string propertyName = GetPropertyNameFromPath(propertyPath);
            if (propertyName == nameof(Element.Start) || propertyName == nameof(Element.Length))
            {
                // 変更前後の両方の範囲を含む
                TimeRange currentRange = element.Range;

                // OldValueから変更前の範囲を計算
                if (updateOp.OldValue is TimeSpan oldTimeSpan)
                {
                    TimeRange oldRange = propertyName == nameof(Element.Start)
                        ? currentRange.WithStart(oldTimeSpan)
                        : currentRange.WithDuration(oldTimeSpan);
                    return currentRange.Union(oldRange);
                }

                return currentRange;
            }

            // その他のElementプロパティの場合
            return element.Range;
        }

        // Video-mute, solo, and audio-mute can all change graphics output (audio-mute
        // via audio-driven visualizers); only lock is editor-only and cache-neutral.
        if (obj is TimelineLayer layer)
        {
            string propertyName = GetPropertyNameFromPath(propertyPath);
            // ZIndex is absent because a layer's ZIndex only changes in
            // LayerMoveService.ApplyMove, whose Element.ZIndex writes already
            // invalidate the same frame ranges via the Element branch above.
            bool affectsGraphics = propertyName is nameof(TimelineLayer.IsVideoMuted)
                or nameof(TimelineLayer.IsSolo)
                or nameof(TimelineLayer.IsAudioMuted);
            if (!affectsGraphics) return null;

            // Solo re-filters every layer; video/audio-mute only affect their own zIndex.
            bool soloChanged = propertyName == nameof(TimelineLayer.IsSolo);
            TimeRange? union = null;
            foreach (Element el in Scene.Children)
            {
                if (!soloChanged && el.ZIndex != layer.ZIndex) continue;
                union = union is { } u ? u.Union(el.Range) : el.Range;
            }

            return union;
        }

        // Element以外のオブジェクトの場合、親を辿ってElementを探す
        Element? parentElement = FindElementFromObject(obj);
        if (parentElement != null)
        {
            return parentElement.Range;
        }

        return null;
    }

    private static string GetPropertyNameFromPath(string propertyPath)
    {
        if (propertyPath.Contains('.'))
        {
            string[] parts = propertyPath.Split('.');
            return parts[^1];
        }

        return propertyPath;
    }

    private static Element? FindElementFromObject(CoreObject obj)
    {
        if (obj is Element element)
        {
            return element;
        }

        if (obj is IHierarchical hierarchical)
        {
            return hierarchical.EnumerateAncestors<Element>().FirstOrDefault();
        }

        return null;
    }

    // Telemetryで使う
    public string SceneId { get; }

    public Scene Scene { get; private set; }

    Scene IAiJobResultEditorContext.Scene => Scene;

    TimeSpan IAiJobResultEditorContext.CurrentTime => Player.CurrentFrame.Value;

    IElementAdder IAiJobResultEditorContext.ElementAdder => _elementAdder;

    int IAiJobResultEditorContext.GetNextLayer(TimeSpan start)
    {
        return Scene.Children
            .Where(item => item.Start <= start && start < item.Range.End)
            .Select(item => item.ZIndex)
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

    public ReadOnlyReactivePropertySlim<FrameCacheManager> FrameCacheManager { get; private set; }

    public EditorExtension Extension => SceneEditorExtension.Instance;

    public IEditorContextCloseService CloseService => _contextCloseService;

    public CoreObject Object => Scene;

    public IKnownEditorCommands? Commands { get; private set; }

    IReactiveProperty<bool> IEditorContext.IsEnabled => IsEnabled;

    internal DockHostViewModel DockHost { get; }

    internal bool IsDisposeRequested
    {
        get
        {
            lock (_disposeGate)
                return _disposeTask is not null;
        }
    }

    internal Action? BeforePreOwnershipCloseStart { get; set; }

    // Admission and completion are serialized with DisposeAsync, while observer callbacks run
    // outside the gate. Teardown drains admitted callbacks after closing further admission.
    internal bool TryPublish(Action publish)
    {
        ArgumentNullException.ThrowIfNull(publish);
        lock (_disposeGate)
        {
            if (_disposeTask is not null)
                return false;

            if (_activePublications++ == 0)
            {
                _publicationDrain = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        try
        {
            publish();
            lock (_disposeGate)
                return _disposeTask is null;
        }
        finally
        {
            TaskCompletionSource? drained = null;
            lock (_disposeGate)
            {
                _activePublications--;
                if (_activePublications == 0)
                {
                    drained = _publicationDrain;
                    _publicationDrain = null;
                }
            }
            drained?.TrySetResult();
        }
    }

    bool IEditorContextPublicationGate.TryPublish(Action publish)
        => TryPublish(publish);

    /// <summary>Requests terminal disposal of the editor context.</summary>
    public ValueTask DisposeAsync() => new(GetOrStartDisposal().Completion);

    private (bool Started, Task Completion) GetOrStartDisposal()
    {
        TaskCompletionSource<object?>? completion = null;
        Task task;
        Task publicationDrain = Task.CompletedTask;
        bool startDisposal = false;
        lock (_disposeGate)
        {
            if (_disposeTask is not null)
            {
                task = _disposeTask;
            }
            else
            {
                completion = new TaskCompletionSource<object?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = completion.Task;
                task = completion.Task;
                publicationDrain = _publicationDrain?.Task ?? Task.CompletedTask;
                startDisposal = true;
            }
        }

        Exception? startupFailure = null;
        if (startDisposal)
        {
            try
            {
                DockHost.RequestOwnerShutdown();
                EditorService.RequestContextShutdown(this);
            }
            catch (Exception ex)
            {
                startupFailure = ex;
            }
            finally
            {
                _ = DisposeCoreAsync(publicationDrain, completion!, startupFailure);
            }
        }
        return (startDisposal, task);
    }

    private async Task DisposeCoreAsync(
        Task publicationDrain,
        TaskCompletionSource<object?> completion,
        Exception? startupFailure)
    {
        List<Exception>? failures = startupFailure is null ? null : [startupFailure];
        _logger.LogInformation("Disposing EditViewModel ({SceneId}).", SceneId);
        Scene scene = Scene;

        await TryAsync(async () => await publicationDrain.ConfigureAwait(false));

        // Block any proxy-invalidation flush already posted to the UI thread from running after this
        // nulls Scene / disposes FrameCacheManager below.
        _disposed = true;
        Try(() => GlobalConfiguration.Instance.EditorConfig.PropertyChanged -= OnEditorConfigPropertyChanged);

        await TryAsync(async () => await _constructionCompleted.Task.ConfigureAwait(false));
        await TryAsync(async () => await _restoreTask.ConfigureAwait(false));
        await TryAsync(async () => await DockHost.WaitForLayoutTransitionAsync().ConfigureAwait(false));
        if (scene.Uri is not null)
            Try(() => SaveState());
        Try(() => _editorSelection.SelectedObject.Value = null);
        // Player を破棄する前にイベント購読を外し、Subject 破棄後の OnNext を抑止する。
        Try(DisposeCommandStateNotifier);
        // Tool contexts can own paid-AI operations that still publish through the editor.
        // Cancel and drain them before retiring element handlers and the player.
        await TryAsync(async () => await DockHost.DisposeAsync());
        // Retire and cancel UI-initiated element operations before waiting for handler leases.
        // Awaiting yields the UI thread so cancellation continuations can release those leases.
        await TryAsync(async () => await _elementAdder.DisposeAsync());
        await TryAsync(async () => await Player.DisposeAsync());
        Try(() => _elementNudgeService?.Dispose());
        Try(() => _historyMutationPlaybackGuard.Dispose());
        Try(() => _disposables.Dispose());
        Try(() => IsEnabled.Dispose());
        Player = null!;
        BufferStatus = null!;

        // History can retain an undone unsaved element across a save. Once the editor closes and
        // history is about to be discarded, no live or redoable item may still own this directory.
        Try(() => UnsavedSceneStorage.Cleanup(scene.Id));
        Scene = null!;
        Commands = null!;
        Try(HistoryManager.Clear);
        Try(() => FrameCacheManager.Value.Dispose());
        Try(() => FrameCacheManager.Dispose());

        _logger.LogInformation("Disposed EditViewModel ({SceneId}).", SceneId);
        if (failures is null)
            completion.TrySetResult(null);
        else
            completion.TrySetException(
                failures.Count == 1 ? failures[0] : new AggregateException(failures));
        return;

        void Try(Action action)
        {
            try { action(); }
            catch (Exception ex) { (failures ??= []).Add(ex); }
        }

        async Task TryAsync(Func<Task> action)
        {
            try { await action().ConfigureAwait(false); }
            catch (Exception ex) { (failures ??= []).Add(ex); }
        }
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

    public ValueTask<bool> OpenToolTabAsync(IToolContext item)
        => new(DockHost.OpenToolTabAsync(item));

    public ValueTask CloseToolTabAsync(IToolContext item)
        => new(DockHost.CloseToolTabAsync(item));

    private string ViewStateDirectory()
    {
        string directory = Path.GetDirectoryName(Scene.Uri!.LocalPath)!;

        directory = Path.Combine(directory, EditorConstants.BeutlFolder, EditorConstants.ViewStateFolder);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return directory;
    }

    private void SaveState(bool isExplicitUserSave = false)
    {
        if (_viewStateSaveSuppressed)
        {
            // RestoreState left an unreadable file in place (transient IO failure or a
            // failed quarantine attempt). Writing default state now would clobber the
            // file the user actually cares about — skip until the next launch. The
            // explicit-save path also surfaces a warning so the user knows their Ctrl+S
            // did not write view state (AutoSave / dispose stay silent — by design).
            if (isExplicitUserSave)
            {
                _logger.LogWarning(
                    "Explicit save requested but view state save is suppressed this session ({SceneId}).",
                    SceneId);
                NotificationService.ShowWarning(string.Empty, MessageStrings.ViewStateSaveSuppressed);
            }
            return;
        }

        string viewStateDir = ViewStateDirectory();
        var json = new JsonObject
        {
            ["selected-object"] = _editorSelection.SelectedObject.Value?.Id,
            ["max-layer-count"] = _timelineOptionsProvider.Options.Value.MaxLayerCount,
            ["scale"] = _timelineOptionsProvider.Options.Value.Scale,
            ["offset"] = new JsonObject { ["x"] = _timelineOptionsProvider.Options.Value.Offset.X, ["y"] = _timelineOptionsProvider.Options.Value.Offset.Y, },
            ["bpm-grid"] = new JsonObject
            {
                ["bpm"] = _timelineOptionsProvider.Options.Value.BpmGrid.Bpm,
                ["subdivisions"] = _timelineOptionsProvider.Options.Value.BpmGrid.Subdivisions,
                ["offset"] = _timelineOptionsProvider.Options.Value.BpmGrid.Offset.ToString("c"),
                ["is-enabled"] = _timelineOptionsProvider.Options.Value.BpmGrid.IsEnabled,
            }
        };

        DockHost.WriteToJson(json);

        json["current-time"] = JsonValue.Create(_editorClock.CurrentTime.Value);

        string name = Path.GetFileNameWithoutExtension(Scene.Uri!.LocalPath);
        json.JsonSave(Path.Combine(viewStateDir, $"{name}.config"));
    }

    private async Task RestoreStateAsync()
    {
        string viewStateDir = ViewStateDirectory();
        string name = Path.GetFileNameWithoutExtension(Scene.Uri!.LocalPath);
        string viewStateFile = Path.Combine(viewStateDir, $"{name}.config");

        if (!File.Exists(viewStateFile))
        {
            _logger.LogInformation("No state file found, opening default tabs.");
            await SafeOpenDefaultTabsAsync();
            return;
        }

        _logger.LogInformation("Restoring state from {ViewStateFile}.", viewStateFile);
        JsonNode? json;
        try
        {
            using var stream = new FileStream(viewStateFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            json = JsonNode.Parse(stream);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "View state file {ViewStateFile} is malformed; quarantining and opening default tabs ({SceneId}).", viewStateFile, SceneId);
            QuarantineCorruptViewState(viewStateFile);
            await SafeOpenDefaultTabsAsync();
            return;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // File existed at File.Exists() but vanished before FileStream could open it
            // (TOCTOU — another process or a user cleanup). Nothing to protect, so treat
            // this like the no-state-file branch and let SaveState() proceed normally.
            _logger.LogWarning(ex, "View state file {ViewStateFile} disappeared before it could be read; opening default tabs ({SceneId}).", viewStateFile, SceneId);
            await SafeOpenDefaultTabsAsync();
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // IO / permission failure (file lock, antivirus, sharing violation, path-too-long,
            // disk error, etc.). The file may still be valid — leave it in place so the next
            // launch can retry, and suppress SaveState() this session so AutoSave does not
            // overwrite it with the default layout before the user gets a chance to recover.
            _logger.LogError(ex, "Failed to read view state file {ViewStateFile}; opening default tabs and suppressing view state save this session ({SceneId}).", viewStateFile, SceneId);
            _viewStateSaveSuppressed = true;
            await SafeOpenDefaultTabsAsync();
            return;
        }

        if (json is not JsonObject jsonObject)
        {
            // JsonNode.Parse returns C# null for the JSON null literal; report that
            // explicitly and fall back to GetValueKind() for the rest (more informative
            // than the runtime type name).
            _logger.LogWarning(
                "View state root is not a JSON object (was {Kind}) in {ViewStateFile}; opening default tabs ({SceneId}).",
                json is null ? nameof(JsonValueKind.Null) : json.GetValueKind().ToString(),
                viewStateFile,
                SceneId);
            QuarantineCorruptViewState(viewStateFile);
            await SafeOpenDefaultTabsAsync();
            return;
        }

        try
        {
            try
            {
                Guid? id = (Guid?)json["selected-object"];
                if (id.HasValue)
                {
                    var searcher = new ObjectSearcher(Scene, o => o is CoreObject obj && obj.Id == id.Value);
                    _editorSelection.SelectedObject.Value = searcher.Search() as CoreObject;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
            {
                // Selection is non-critical state; let the rest of restore continue.
                // OOM / cancellation propagate so the deeper catch can quarantine.
                _logger.LogWarning(ex, "Could not restore the selected object from {ViewStateFile}; selection cleared ({SceneId}).", viewStateFile, SceneId);
            }

            var timelineOptions = new TimelineOptions();

            if (jsonObject.TryGetPropertyValue("max-layer-count", out JsonNode? maxLayer)
                && maxLayer is JsonValue maxLayerValue
                && maxLayerValue.TryGetValue(out int maxLayerCount))
            {
                timelineOptions = timelineOptions with { MaxLayerCount = maxLayerCount };
            }

            if (jsonObject.TryGetPropertyValue("scale", out JsonNode? scaleNode)
                && scaleNode is JsonValue scaleValue
                && scaleValue.TryGetValue(out float scale))
            {
                timelineOptions = timelineOptions with { Scale = scale };
            }

            if (jsonObject.TryGetPropertyValue("offset", out JsonNode? offsetNode)
                && offsetNode is JsonObject offsetObj
                && offsetObj.TryGetPropertyValueAsJsonValue("x", out float x)
                && offsetObj.TryGetPropertyValueAsJsonValue("y", out float y))
            {
                timelineOptions = timelineOptions with { Offset = new Vector2(x, y) };
            }

            if (jsonObject.TryGetPropertyValue("bpm-grid", out JsonNode? bpmGridNode)
                && bpmGridNode is JsonObject bpmGridObj)
            {
                var bpmGrid = new BpmGridOptions();

                if (bpmGridObj.TryGetPropertyValueAsJsonValue("bpm", out double bpm))
                    bpmGrid = bpmGrid with { Bpm = bpm };

                if (bpmGridObj.TryGetPropertyValueAsJsonValue("subdivisions", out int subdivisions))
                    bpmGrid = bpmGrid with { Subdivisions = subdivisions };

                if (bpmGridObj.TryGetPropertyValueAsJsonValue("offset", out string? bpmOffsetStr)
                    && TimeSpan.TryParseExact(bpmOffsetStr, "c", CultureInfo.InvariantCulture, out TimeSpan bpmOffset))
                    bpmGrid = bpmGrid with { Offset = bpmOffset };

                if (bpmGridObj.TryGetPropertyValueAsJsonValue("is-enabled", out bool isEnabled))
                    bpmGrid = bpmGrid with { IsEnabled = isEnabled };

                timelineOptions = timelineOptions with { BpmGrid = bpmGrid };
            }

            _timelineOptionsProvider.Options.Value = timelineOptions;

            await DockHost.ReadFromJsonAsync(jsonObject);

            if (jsonObject.TryGetPropertyValueAsJsonValue("current-time", out string? currentTimeStr)
                && TimeSpan.TryParse(currentTimeStr, out TimeSpan currentTime))
            {
                _editorClock.CurrentTime.Value = currentTime;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            // The file parsed as JSON but a deeper restore step blew up — treat the
            // file as effectively corrupt so the next AutoSave does not silently
            // overwrite it with the default layout. OOM / cancellation propagate
            // because they signal "abort this restore", not "the file is bad" —
            // quarantining a valid file on those would permanently lose the layout.
            _logger.LogError(ex, "Unexpected error while restoring view state from {ViewStateFile}; quarantining and opening default tabs ({SceneId}).", viewStateFile, SceneId);
            QuarantineCorruptViewState(viewStateFile);
            await SafeOpenDefaultTabsAsync();
        }
    }

    private async Task SafeOpenDefaultTabsAsync()
    {
        // OpenDefaultTabs runs arbitrary tool-extension code, so swallow recoverable
        // exceptions here — the scene must remain openable even when the
        // default-layout fallback itself fails. OOM / cancellation propagate.
        try
        {
            await DockHost.OpenDefaultTabsAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to open default tabs ({SceneId}).", SceneId);
            NotificationService.ShowError(string.Empty, MessageStrings.DefaultTabsOpenFailed);
        }
    }

    private void QuarantineCorruptViewState(string viewStateFile)
    {
        // Move the unreadable file aside so subsequent SaveState() calls (AutoSave or
        // dispose) do not overwrite it with the default layout and erase the user's
        // customizations. The 8-hex-char random suffix (~4 billion variants per second)
        // prevents collisions between concurrent restores or other Beutl instances
        // that hit corruption with the same one-second timestamp.
        try
        {
            string suffix = Guid.NewGuid().ToString("N")[..8];
            string quarantined = $"{viewStateFile}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}-{suffix}";
            File.Move(viewStateFile, quarantined);
            _logger.LogInformation("Moved unreadable view state to {QuarantinedFile} ({SceneId}).", quarantined, SceneId);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // The file (or its directory) vanished between the parse failure and the
            // move — nothing left to protect, so do NOT suppress SaveState(); a later
            // save can write fresh state to the now-empty path normally.
            _logger.LogWarning(ex, "View state file {ViewStateFile} disappeared before it could be quarantined ({SceneId}).", viewStateFile, SceneId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Expected move failures (sharing violation, permission denied, etc.) where
            // the corrupt file likely still occupies the original path; suppress
            // SaveState() so it does not get overwritten before a developer can recover
            // the original for diagnostics.
            _logger.LogWarning(ex, "Failed to quarantine view state file {ViewStateFile}; suppressing view state save this session ({SceneId}).", viewStateFile, SceneId);
            _viewStateSaveSuppressed = true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            // Belt-and-suspenders: any other File.Move failure (NotSupportedException,
            // ArgumentException, etc.) must still leave us in a state where AutoSave
            // cannot overwrite the file we tried to protect.
            _logger.LogError(ex, "Unexpected failure quarantining view state file {ViewStateFile}; suppressing view state save this session ({SceneId}).", viewStateFile, SceneId);
            _viewStateSaveSuppressed = true;
        }
    }

    private sealed class BoundContextCloseService(
        EditViewModel owner,
        IEditorContextCloseService closeService) : IEditorContextCloseService
    {
        public EditorContextHostToken HostToken => closeService.HostToken;

        public EditorContextCloseRequest RequestClose(IEditorContext context)
        {
            if (!ReferenceEquals(context, owner))
            {
                return new EditorContextCloseRequest(
                    EditorContextCloseRequestStatus.NotOwned,
                    Task.CompletedTask);
            }

            EditorContextCloseRequest request = closeService.RequestClose(owner);
            if (request.Status != EditorContextCloseRequestStatus.NotOwned)
                return request;

            owner.BeforePreOwnershipCloseStart?.Invoke();
            (bool started, Task completion) = owner.GetOrStartDisposal();
            EditorContextCloseRequest attachedRequest = closeService.RequestClose(owner);
            if (attachedRequest.Status != EditorContextCloseRequestStatus.NotOwned)
                return attachedRequest;
            _ = completion.ContinueWith(
                task => owner._logger.LogError(
                    task.Exception,
                    "Requested editor close failed ({SceneId}).",
                    owner.SceneId),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return new EditorContextCloseRequest(
                started
                    ? EditorContextCloseRequestStatus.Accepted
                    : EditorContextCloseRequestStatus.AlreadyClosing,
                completion);
        }
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(Scene))
            return Scene;

        if (serviceType.IsAssignableTo(typeof(IEditorContext)))
            return this;

        if (serviceType == typeof(IEditorContextCloseService))
            return _contextCloseService;

        if (serviceType == typeof(HistoryManager))
            return HistoryManager;

        if (serviceType.IsAssignableTo(typeof(ITimelineOptionsProvider)))
            return _timelineOptionsProvider;

        if (serviceType.IsAssignableTo(typeof(IEditorClock)))
            return _editorClock;

        if (serviceType.IsAssignableTo(typeof(IEditorSelection)))
            return _editorSelection;

        if (serviceType.IsAssignableTo(typeof(IElementAdder)))
            return _elementAdder;

        if (serviceType.IsAssignableTo(typeof(ISceneTimeRangeService)))
            return _sceneTimeRangeService ??= new SceneTimeRangeService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(IElementResizeService)))
            return _elementResizeService ??= new ElementResizeService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(IElementSlipService)))
            return _elementSlipService ??= new ElementSlipService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(IElementDuplicateService)))
            return _elementDuplicateService ??= new ElementDuplicateService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(IElementMoveService)))
            return _elementMoveService ??= new ElementMoveService(
                HistoryManager,
                (IElementDuplicateService)GetService(typeof(IElementDuplicateService))!);

        if (serviceType.IsAssignableTo(typeof(IElementGapService)))
            return _elementGapService ??= new ElementGapService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(IClipboardGateway)))
            return _clipboardGateway;

        if (serviceType.IsAssignableTo(typeof(IElementClipboardService)))
            return _elementClipboardService ??= _clipboardGateway is null
                ? null!
                : new ElementClipboardService(
                    HistoryManager,
                    _clipboardGateway,
                    (IElementDuplicateService)GetService(typeof(IElementDuplicateService))!,
                    static () => Beutl.Editor.Components.Helpers.ColorGenerator.GenerateColor(
                        typeof(Beutl.Graphics.SourceImage).FullName!),
                    _elementAdder);

        if (serviceType.IsAssignableTo(typeof(IElementStructureService)))
            return _elementStructureService ??= new ElementStructureService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(IElementAttributeService)))
            return _elementAttributeService ??= new ElementAttributeService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(IElementNudgeService)))
            return _elementNudgeService ??= CreateNudgeService();

        if (serviceType.IsAssignableTo(typeof(ILayerMoveService)))
            return _layerMoveService ??= new LayerMoveService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(ILayerAttributeService)))
            return _layerAttributeService ??= new LayerAttributeService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(ISceneSettingsService)))
            return _sceneSettingsService ??= new SceneSettingsService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(IKeyFrameClipboardService)))
            return _keyFrameClipboardService ??= new KeyFrameClipboardService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(INodeGraphMutationService)))
            return _nodeGraphMutationService ??= new NodeGraphMutationService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(IElementObjectService)))
            return _elementObjectService ??= new ElementObjectService(HistoryManager);

        if (serviceType == typeof(PlayerViewModel) || serviceType.IsAssignableTo(typeof(IPreviewPlayer)))
            return Player;

        if (serviceType == typeof(IPreviewRenderQuality))
            return this;

        if (serviceType == typeof(FrameCacheManager))
            return FrameCacheManager.Value;

        if (serviceType.IsAssignableTo(typeof(IBufferStatus)))
            return BufferStatus;

        if (serviceType == typeof(Beutl.Api.Services.ExtensionProvider))
            return ExtensionProvider;

        if (serviceType.IsAssignableTo(typeof(IPropertyEditorFactory)))
            return _propertyEditorFactory ??= new Services.Adapters.PropertyEditorFactoryAdapter(ExtensionProvider);

        if (serviceType.IsAssignableTo(typeof(IPropertiesEditorFactory)))
            return _propertiesEditorFactory ??= new Services.Adapters.PropertiesEditorFactoryImpl(ExtensionProvider);

        // Hand out the swap-stable facades so a cached reference survives a store-root / cap change; the
        // concrete ProxyEvictionService request is the exception (no facade for the concrete type).
        if (serviceType.IsAssignableTo(typeof(IProxyStore)))
            return ProxyMediaServices.Current?.StoreFacade;

        if (serviceType.IsAssignableTo(typeof(IProxyResolver)))
            return ProxyMediaServices.Current?.ResolverFacade;

        if (serviceType.IsAssignableTo(typeof(IProxyJobQueue)))
            return ProxyMediaServices.Current?.QueueFacade;

        if (serviceType == typeof(ProxyEvictionService))
            return ProxyMediaServices.Current?.EvictionService;

        if (serviceType.IsAssignableTo(typeof(IProxyStoreCapInfo)))
            return ProxyMediaServices.Current?.CapInfoFacade;

        return null;
    }

    private ElementNudgeService CreateNudgeService()
    {
        // The debounce timer fires off the UI thread; post the commit back so it serializes
        // with other editing ops.
        var nudge = new ElementNudgeService(HistoryManager, action => Dispatcher.UIThread.Post(action));
        // Drain pending nudges before Undo / Redo / JumpTo so they don't merge into the next
        // history transaction.
        HistoryManager.BeforeMutation
            .Subscribe(_ => nudge.Flush())
            .DisposeWith(_disposables);
        return nudge;
    }

    internal ValueTask<bool> UndoHistoryAsync()
    {
        return ExecuteHistoryMutationAsync(
            "Undo",
            "Undoing last command.",
            "Undo completed.",
            // A pending transaction is flushed onto the undo stack by BeforeMutation
            // before Undo() runs, so it can revert scene state even when CanUndo is false.
            () => HistoryManager.CanUndo || HistoryManager.HasPendingOperations,
            HistoryManager.Undo);
    }

    internal ValueTask<bool> RedoHistoryAsync()
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
            () => HistoryManager.WouldJumpToMove(index),
            () => HistoryManager.JumpTo(index));
    }

    private async ValueTask<bool> ExecuteHistoryMutationAsync(
        string operationName,
        string? startMessage,
        string? completedMessage,
        Func<bool> shouldPause,
        Func<bool> mutate)
    {
        try
        {
            if (startMessage is not null)
            {
                _logger.LogInformation("{Message}", startMessage);
            }

            bool changed = await _historyMutationPlaybackGuard.RunAsync(
                Player, HistoryManager.FlushPendingMutations, shouldPause, mutate);
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
            NotificationService.ShowError(Strings.History, Strings.History_OperationFailed);
            return false;
        }
    }

    private sealed class KnownCommandsImpl(Scene scene, EditViewModel viewModel) : IKnownEditorCommands
    {
        public ValueTask<bool> OnSave()
        {
            viewModel._logger.LogInformation("Saving scene ({SceneId}).", scene.Id);
            Uri sceneUri = scene.Uri
                ?? throw new InvalidOperationException("An unsaved scene needs a destination before it can be saved.");
            UnsavedSceneStorage.SaveRelocation relocation =
                UnsavedSceneStorage.PrepareSave(scene, sceneUri);
            try
            {
                relocation.Apply();
                Parallel.ForEach(scene.Children, item => CoreSerializer.StoreToUri(item, item.Uri!));
                // The scene is the commit record for every child/resource URI. Persist it only
                // after every referenced file is durable at its new location.
                CoreSerializer.StoreToUri(scene, sceneUri, CoreSerializationMode.Write);
            }
            catch
            {
                relocation.Rollback();
                throw;
            }

            relocation.Commit();
            viewModel.SaveState(isExplicitUserSave: true);
            viewModel._logger.LogInformation("Scene ({SceneId}) saved successfully.", scene.Id);

            return ValueTask.FromResult(true);
        }

        public async ValueTask<bool> OnUndo()
        {
            return await viewModel.UndoHistoryAsync();
        }

        public async ValueTask<bool> OnRedo()
        {
            return await viewModel.RedoHistoryAsync();
        }
    }
}
