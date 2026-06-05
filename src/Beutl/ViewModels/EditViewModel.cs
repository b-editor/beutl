using System.ComponentModel;
using System.Numerics;
using System.Security;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Operations;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Microsoft.Extensions.Logging;
using System.Reactive.Linq;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Dispatcher = Avalonia.Threading.Dispatcher;

namespace Beutl.ViewModels;

public sealed partial class EditViewModel : IEditorContext, ISupportAutoSaveEditorContext
{
    private readonly ILogger _logger = Log.CreateLogger<EditViewModel>();
    private readonly AutoSaveService _autoSaveService = new();

    private readonly CompositeDisposable _disposables = [];
    private readonly TimelineOptionsProviderImpl _timelineOptionsProvider;
    private readonly EditorClockImpl _editorClock;
    private readonly EditorSelectionImpl _editorSelection;
    private readonly ElementAdderImpl _elementAdder;
    private SceneTimeRangeService? _sceneTimeRangeService;
    private ElementResizeService? _elementResizeService;
    private ElementDuplicateService? _elementDuplicateService;
    private ElementMoveService? _elementMoveService;
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
    private volatile bool _viewStateSaveSuppressed;

    public EditViewModel(Scene scene)
    {
        _logger.LogInformation("Initializing EditViewModel for Scene ({SceneId}).", scene.Id);

        Scene = scene;
        SceneId = scene.Id.ToString();

        _timelineOptionsProvider = new TimelineOptionsProviderImpl(scene)
            .DisposeWith(_disposables);
        _editorClock = new EditorClockImpl(scene)
            .DisposeWith(_disposables);
        _editorSelection = new EditorSelectionImpl()
            .DisposeWith(_disposables);

        // feature 003 (US4): per-edit-view, NON-persisted preview render quality. Changing it rebuilds
        // the renderer + frame cache at the new output scale (SaveState/RestoreState never touch it).
        PreviewScale = new ReactivePropertySlim<RenderScale>(RenderScale.Full)
            .DisposeWith(_disposables);

        IObservable<(PixelSize FrameSize, RenderScale Scale)> frameSizeAndScale =
            scene.GetObservable(Scene.FrameSizeProperty)
                .CombineLatest(PreviewScale, (frameSize, scale) => (frameSize, scale));

        Renderer = frameSizeAndScale
            .Select(t => new SceneRenderer(Scene, t.Scale.ToFloat(t.FrameSize, t.FrameSize.ToSize(1))))
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;
        Composer = Renderer.Select(v => new SceneComposer(Scene))
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;

        FrameCacheManager = frameSizeAndScale
            .Select(t => new FrameCacheManager(t.FrameSize, CreateFrameCacheOptions()) { IsEnabled = config.IsFrameCacheEnabled })
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        config.PropertyChanged += OnEditorConfigPropertyChanged;

        Player = new PlayerViewModel(this);
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

        DockHost = new DockHostViewModel(SceneId, this)
            .DisposeWith(_disposables);

        _elementAdder = new ElementAdderImpl(this);
        _clipboardGateway = new Beutl.Editor.Components.Services.AvaloniaClipboardGateway();

        _autoSaveService.SaveError
            .Subscribe(_ =>
                NotificationService.ShowError(string.Empty, MessageStrings.FileSaveException))
            .DisposeWith(_disposables);
        _autoSaveService.DisposeWith(_disposables);

        RestoreState();

        _logger.LogInformation("Initialized EditViewModel for Scene ({SceneId}).", SceneId);
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

    public ReadOnlyReactivePropertySlim<SceneRenderer> Renderer { get; }

    /// <summary>
    /// Per-edit-view preview render quality (feature 003, US4). Non-persisted: defaults to
    /// <see cref="RenderScale.Full"/> every session and is never written by SaveState/RestoreState.
    /// Changing it atomically rebuilds <see cref="Renderer"/> and <see cref="FrameCacheManager"/>.
    /// </summary>
    public ReactivePropertySlim<RenderScale> PreviewScale { get; }

    public ReadOnlyReactivePropertySlim<SceneComposer> Composer { get; }

    public ReactivePropertySlim<bool> IsEnabled { get; } = new(true);

    public PlayerViewModel Player { get; private set; }

    public BufferStatusViewModel BufferStatus { get; private set; }

    public HistoryManager HistoryManager { get; private set; }

    public ReadOnlyReactivePropertySlim<FrameCacheManager> FrameCacheManager { get; private set; }

    public EditorExtension Extension => SceneEditorExtension.Instance;

    public CoreObject Object => Scene;

    public IKnownEditorCommands? Commands { get; private set; }

    IReactiveProperty<bool> IEditorContext.IsEnabled => IsEnabled;

    public DockHostViewModel DockHost { get; }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing EditViewModel ({SceneId}).", SceneId);

        GlobalConfiguration.Instance.EditorConfig.PropertyChanged -= OnEditorConfigPropertyChanged;
        SaveState();
        _editorSelection.SelectedObject.Value = null;
        // Player を破棄する前にイベント購読を外し、Subject 破棄後の OnNext を抑止する。
        DisposeCommandStateNotifier();
        await Player.DisposeAsync();
        _elementNudgeService?.Dispose();
        _disposables.Dispose();
        IsEnabled.Dispose();
        Player = null!;
        BufferStatus = null!;

        Scene = null!;
        Commands = null!;
        HistoryManager.Clear();
        FrameCacheManager.Value.Dispose();
        FrameCacheManager.Dispose();

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

    private void RestoreState()
    {
        string viewStateDir = ViewStateDirectory();
        string name = Path.GetFileNameWithoutExtension(Scene.Uri!.LocalPath);
        string viewStateFile = Path.Combine(viewStateDir, $"{name}.config");

        if (!File.Exists(viewStateFile))
        {
            _logger.LogInformation("No state file found, opening default tabs.");
            SafeOpenDefaultTabs();
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
            SafeOpenDefaultTabs();
            return;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // File existed at File.Exists() but vanished before FileStream could open it
            // (TOCTOU — another process or a user cleanup). Nothing to protect, so treat
            // this like the no-state-file branch and let SaveState() proceed normally.
            _logger.LogWarning(ex, "View state file {ViewStateFile} disappeared before it could be read; opening default tabs ({SceneId}).", viewStateFile, SceneId);
            SafeOpenDefaultTabs();
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
            SafeOpenDefaultTabs();
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
            SafeOpenDefaultTabs();
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

            DockHost.ReadFromJson(jsonObject);

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
            SafeOpenDefaultTabs();
        }
    }

    private void SafeOpenDefaultTabs()
    {
        // OpenDefaultTabs runs arbitrary tool-extension code, so swallow recoverable
        // exceptions here — the scene must remain openable even when the
        // default-layout fallback itself fails. OOM / cancellation propagate.
        try
        {
            DockHost.OpenDefaultTabs();
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

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(Scene))
            return Scene;

        if (serviceType.IsAssignableTo(typeof(IEditorContext)))
            return this;

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

        if (serviceType.IsAssignableTo(typeof(IElementDuplicateService)))
            return _elementDuplicateService ??= new ElementDuplicateService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(IElementMoveService)))
            return _elementMoveService ??= new ElementMoveService(
                HistoryManager,
                (IElementDuplicateService)GetService(typeof(IElementDuplicateService))!);

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

        if (serviceType == typeof(FrameCacheManager))
            return FrameCacheManager.Value;

        if (serviceType.IsAssignableTo(typeof(IBufferStatus)))
            return BufferStatus;

        if (serviceType.IsAssignableTo(typeof(IPropertyEditorFactory)))
            return Services.Adapters.PropertyEditorFactoryAdapter.Instance;

        if (serviceType.IsAssignableTo(typeof(IPropertiesEditorFactory)))
            return Services.Adapters.PropertiesEditorFactoryImpl.Instance;

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

    private sealed class KnownCommandsImpl(Scene scene, EditViewModel viewModel) : IKnownEditorCommands
    {
        public ValueTask<bool> OnSave()
        {
            viewModel._logger.LogInformation("Saving scene ({SceneId}).", scene.Id);
            CoreSerializer.StoreToUri(scene, scene.Uri!);
            Parallel.ForEach(scene.Children, item => CoreSerializer.StoreToUri(item, item.Uri!));
            viewModel.SaveState(isExplicitUserSave: true);
            viewModel._logger.LogInformation("Scene ({SceneId}) saved successfully.", scene.Id);

            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> OnUndo()
        {
            viewModel._logger.LogInformation("Undoing last command.");
            viewModel.HistoryManager.Undo();
            viewModel._logger.LogInformation("Undo completed.");

            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> OnRedo()
        {
            viewModel._logger.LogInformation("Redoing last undone command.");
            viewModel.HistoryManager.Redo();
            viewModel._logger.LogInformation("Redo completed.");

            return ValueTask.FromResult(true);
        }
    }
}
