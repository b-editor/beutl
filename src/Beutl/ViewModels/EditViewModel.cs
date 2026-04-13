using System.ComponentModel;
using System.Numerics;
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

        Renderer = scene.GetObservable(Scene.FrameSizeProperty).Select(_ => new SceneRenderer(Scene))
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;
        Composer = Renderer.Select(v => new SceneComposer(Scene))
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;

        FrameCacheManager = scene.GetObservable(Scene.FrameSizeProperty)
            .Select(v => new FrameCacheManager(v, CreateFrameCacheOptions()) { IsEnabled = config.IsFrameCacheEnabled })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        config.PropertyChanged += OnEditorConfigPropertyChanged;

        Player = new PlayerViewModel(this);
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

    public ReactivePropertySlim<TimeSpan> CurrentTime => _editorClock.CurrentTime;

    // Timelineの横幅をmax(MaximumTime, start+duration)で決める
    public ReactivePropertySlim<TimeSpan> MaximumTime => _editorClock.MaximumTime;

    public ReadOnlyReactivePropertySlim<SceneRenderer> Renderer { get; }

    public ReadOnlyReactivePropertySlim<SceneComposer> Composer { get; }

    public ReactiveProperty<CoreObject?> SelectedObject => _editorSelection.SelectedObject;

    public ReactivePropertySlim<bool> IsEnabled { get; } = new(true);

    public ReadOnlyReactivePropertySlim<int?> SelectedLayerNumber => _editorSelection.SelectedLayerNumber;

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
        SelectedObject.Value = null;
        await Player.DisposeAsync();
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

        directory = Path.Combine(directory, Constants.BeutlFolder, Constants.ViewStateFolder);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return directory;
    }

    private void SaveState()
    {
        string viewStateDir = ViewStateDirectory();
        var json = new JsonObject
        {
            ["selected-object"] = SelectedObject.Value?.Id,
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

        json["current-time"] = JsonValue.Create(CurrentTime.Value);

        string name = Path.GetFileNameWithoutExtension(Scene.Uri!.LocalPath);
        json.JsonSave(Path.Combine(viewStateDir, $"{name}.config"));
    }

    private void RestoreState()
    {
        string viewStateDir = ViewStateDirectory();
        string name = Path.GetFileNameWithoutExtension(Scene.Uri!.LocalPath);
        string viewStateFile = Path.Combine(viewStateDir, $"{name}.config");

        if (File.Exists(viewStateFile))
        {
            _logger.LogInformation("Restoring state from {ViewStateFile}.", viewStateFile);
            using var stream = new FileStream(viewStateFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            var json = JsonNode.Parse(stream);
            if (json is not JsonObject jsonObject)
                return;

            try
            {
                Guid? id = (Guid?)json["selected-object"];
                if (id.HasValue)
                {
                    var searcher = new ObjectSearcher(Scene, o => o is CoreObject obj && obj.Id == id.Value);
                    SelectedObject.Value = searcher.Search() as CoreObject;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "An error occurred while restoring the selected object.");
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
                CurrentTime.Value = currentTime;
            }
        }
        else
        {
            _logger.LogInformation("No state file found, opening default tabs.");
            DockHost.OpenDefaultTabs();
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

    private sealed class KnownCommandsImpl(Scene scene, EditViewModel viewModel) : IKnownEditorCommands
    {
        public ValueTask<bool> OnSave()
        {
            viewModel._logger.LogInformation("Saving scene ({SceneId}).", scene.Id);
            CoreSerializer.StoreToUri(scene, scene.Uri!);
            Parallel.ForEach(scene.Children, item => CoreSerializer.StoreToUri(item, item.Uri!));
            viewModel.SaveState();
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
