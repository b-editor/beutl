using System.Collections.Immutable;
using System.ComponentModel;
using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Beutl.Animation;
using Beutl.Configuration;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Transformation;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.Models;
using Beutl.Operation;
using Beutl.Operators.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.Tools;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using LibraryService = Beutl.Services.LibraryService;

namespace Beutl.ViewModels;

public sealed partial class EditViewModel : IEditorContext, ITimelineOptionsProvider, ISupportCloseAnimation,
    ISupportAutoSaveEditorContext
{
    private readonly ILogger _logger = Log.CreateLogger<EditViewModel>();

    private readonly CompositeDisposable _disposables = [];

    public EditViewModel(Scene scene)
    {
        _logger.LogInformation("Initializing EditViewModel for Scene ({SceneId}).", scene.Id);

        Scene = scene;
        SceneId = scene.Id.ToString();
        Scene.Children.Attached += OnElementAttached;
        Scene.Children.Detached += OnElementDetached;
        CurrentTime = new ReactivePropertySlim<TimeSpan>()
            .DisposeWith(_disposables);
        MaximumTime = new ReactivePropertySlim<TimeSpan>()
            .DisposeWith(_disposables);
        CalculateMaximumTime();
        Renderer = scene.GetObservable(Scene.FrameSizeProperty).Select(_ => new SceneRenderer(Scene))
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;
        Composer = Renderer.Select(v => new SceneComposer(Scene, v))
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;

        FrameCacheManager = scene.GetObservable(Scene.FrameSizeProperty)
            .Select(v => new FrameCacheManager(v, CreateFrameCacheOptions()) { IsEnabled = config.IsFrameCacheEnabled })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        config.PropertyChanged += OnEditorConfigPropertyChanged;

        SelectedObject = new ReactiveProperty<CoreObject?>()
            .DisposeWith(_disposables);

        Scale = Options.Select(o => o.Scale);
        Offset = Options.Select(o => o.Offset);
        SelectedObject.CombineWithPrevious()
            .Subscribe(v =>
            {
                if (v.OldValue is IHierarchical oldHierarchical)
                    oldHierarchical.DetachedFromHierarchy -= OnSelectedObjectDetachedFromHierarchy;

                if (v.NewValue is IHierarchical newHierarchical)
                    newHierarchical.DetachedFromHierarchy += OnSelectedObjectDetachedFromHierarchy;
            })
            .DisposeWith(_disposables);

        SelectedLayerNumber = SelectedObject.Select(v =>
                (v as Element)?.GetObservable(Element.ZIndexProperty).Select(i => (int?)i) ??
                Observable.ReturnThenNever<int?>(null))
            .Switch()
            .ToReadOnlyReactivePropertySlim();

        Player = new PlayerViewModel(this);
        Commands = new KnownCommandsImpl(scene, this);
        CommandRecorder = new CommandRecorder();
        BufferStatus = new BufferStatusViewModel(this)
            .DisposeWith(_disposables);

        DockHost = new DockHostViewModel(SceneId, this)
            .DisposeWith(_disposables);

        CommandRecorder.Executed += OnCommandRecorderExecuted;

        RestoreState();

        _logger.LogInformation("Initialized EditViewModel for Scene ({SceneId}).", SceneId);
    }

    private void OnElementDetached(Element obj)
    {
        obj.PropertyChanged -= OnElementPropertyChanged;

        if (MaximumTime.Value < obj.Range.End)
        {
            MaximumTime.Value = obj.Range.End;
        }
        else
        {
            CalculateMaximumTime();
        }
    }

    private void OnElementAttached(Element obj)
    {
        obj.PropertyChanged += OnElementPropertyChanged;

        if (MaximumTime.Value < obj.Range.End)
        {
            MaximumTime.Value = obj.Range.End;
        }
        else
        {
            CalculateMaximumTime();
        }
    }

    private void CalculateMaximumTime()
    {
        MaximumTime.Value = Scene.Children.Count > 0
            ? Scene.Children.Max(i => i.Range.End)
            : TimeSpan.Zero;
    }

    private void OnElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e is CorePropertyChangedEventArgs<TimeSpan> typedArgs)
        {
            bool startChanged = typedArgs.Property.Id == Element.StartProperty.Id;
            bool lengthChanged = typedArgs.Property.Id == Element.LengthProperty.Id;

            if (sender is Element element && (startChanged || lengthChanged))
            {
                // 変更前の値を取得
                TimeRange oldRange = element.Range;
                if (startChanged) oldRange = oldRange.WithStart(typedArgs.OldValue);
                if (lengthChanged) oldRange = oldRange.WithDuration(typedArgs.OldValue);

                if (MaximumTime.Value < element.Range.End)
                {
                    MaximumTime.Value = element.Range.End;
                }
                else if (MaximumTime.Value == oldRange.End)
                {
                    CalculateMaximumTime();
                }
            }
        }
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
                _logger.LogInformation("Updating RenderNodeCacheContext options due to EditorConfig change.");
                RenderNodeCacheContext? cacheContext = Renderer.Value.GetCacheContext();
                if (cacheContext != null)
                {
                    cacheContext.CacheOptions = RenderCacheOptions.CreateFromGlobalConfiguration();
                }
            }
        }
    }

    private void OnCommandRecorderExecuted(object? sender, CommandExecutedEventArgs e)
    {
        _logger.LogInformation("Command executed, updating FrameCacheManager and saving state if needed.");
        Task.Run(() =>
        {
            int rate = Player.GetFrameRate();
            IEnumerable<TimeRange> affectedRange = e.Command is IAffectsTimelineCommand affectsTimeline
                ? affectsTimeline.GetAffectedRange()
                : e.Storables.OfType<Element>().Select(v => v.Range);

            FrameCacheManager.Value.DeleteAndUpdateBlocks(affectedRange
                .Select(item => (Start: (int)item.Start.ToFrameNumber(rate),
                    End: (int)Math.Ceiling(item.End.ToFrameNumber(rate)))));
        });

        if (GlobalConfiguration.Instance.EditorConfig.IsAutoSaveEnabled)
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                var objects = e.Storables
                    .Select(item =>
                    {
                        if (item is Hierarchical hierarchical)
                        {
                            return hierarchical.EnumerateAncestors<CoreObject>().Where(o => o.Uri != null);
                        }

                        if (item.Uri != null)
                        {
                            return [item];
                        }

                        return [];
                    })
                    .SelectMany(e => e)
                    .ToHashSet();

                foreach (CoreObject item in objects)
                {
                    try
                    {
                        _logger.LogTrace("Auto-saving object ({TypeName}, {ObjectId}).", TypeFormat.ToString(item.GetType()), item.Id);
                        CoreSerializer.StoreToUri(item, item.Uri!, CoreSerializationMode.Write);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "An exception occurred while saving the file.");
                        NotificationService.ShowError(string.Empty,
                            Message.An_exception_occurred_while_saving_the_file);
                    }
                }

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
    }

    private void OnSelectedObjectDetachedFromHierarchy(object? sender, HierarchyAttachmentEventArgs e)
    {
        _logger.LogInformation("Selected object detached from hierarchy, clearing selection.");
        SelectedObject.Value = null;
    }

    // Telemetryで使う
    public string SceneId { get; }

    public Scene Scene { get; private set; }

    public ReactivePropertySlim<TimeSpan> CurrentTime { get; }

    // Timelineの横幅をmax(MaximumTime, start+duration)で決める
    public ReactivePropertySlim<TimeSpan> MaximumTime { get; }

    public ReadOnlyReactivePropertySlim<SceneRenderer> Renderer { get; }

    public ReadOnlyReactivePropertySlim<SceneComposer> Composer { get; }

    public ReactiveProperty<CoreObject?> SelectedObject { get; }

    public ReactivePropertySlim<bool> IsEnabled { get; } = new(true);

    public ReadOnlyReactivePropertySlim<int?> SelectedLayerNumber { get; }

    public PlayerViewModel Player { get; private set; }

    public BufferStatusViewModel BufferStatus { get; private set; }

    public CommandRecorder CommandRecorder { get; private set; }

    public ReadOnlyReactivePropertySlim<FrameCacheManager> FrameCacheManager { get; private set; }

    public EditorExtension Extension => SceneEditorExtension.Instance;

    public CoreObject Object => Scene;

    public IKnownEditorCommands? Commands { get; private set; }

    public IReactiveProperty<TimelineOptions> Options { get; } =
        new ReactiveProperty<TimelineOptions>(new TimelineOptions());

    public IObservable<float> Scale { get; }

    public IObservable<Vector2> Offset { get; }

    IReactiveProperty<bool> IEditorContext.IsEnabled => IsEnabled;

    public DockHostViewModel DockHost { get; }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing EditViewModel ({SceneId}).", SceneId);
        foreach (Element element in Scene.Children)
        {
            element.PropertyChanged -= OnElementPropertyChanged;
        }

        Scene.Children.Attached -= OnElementAttached;
        Scene.Children.Detached -= OnElementDetached;
        GlobalConfiguration.Instance.EditorConfig.PropertyChanged -= OnEditorConfigPropertyChanged;
        SaveState();
        await Player.DisposeAsync();
        _disposables.Dispose();
        Options.Dispose();
        IsEnabled.Dispose();
        Player = null!;
        BufferStatus = null!;

        SelectedObject.Value = null;

        Scene = null!;
        Commands = null!;
        CommandRecorder.Executed -= OnCommandRecorderExecuted;
        CommandRecorder.Clear();
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
            ["max-layer-count"] = Options.Value.MaxLayerCount,
            ["scale"] = Options.Value.Scale,
            ["offset"] = new JsonObject { ["x"] = Options.Value.Offset.X, ["y"] = Options.Value.Offset.Y, }
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

            Options.Value = timelineOptions;

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

        if (serviceType.IsAssignableTo(typeof(ISupportCloseAnimation)))
            return this;

        if (serviceType == typeof(CommandRecorder))
            return CommandRecorder;

        return null;
    }

    public void AddElement(ElementDescription desc)
    {
        _logger.LogInformation("Adding new element with description: {Description}", desc);

        Element CreateElement()
        {
            _logger.LogDebug("Creating new element with start: {Start}, length: {Length}, layer: {Layer}", desc.Start,
                desc.Length, desc.Layer);
            return new Element()
            {
                Start = desc.Start,
                Length = desc.Length,
                ZIndex = desc.Layer,
                Uri = RandomFileNameGenerator.GenerateUri(Scene.Uri!, Constants.ElementFileExtension)
            };
        }

        void SetAccentColor(Element element, string str)
        {
            _logger.LogDebug("Setting accent color for element: {Element}, color string: {ColorString}", element, str);
            element.AccentColor = ColorGenerator.GenerateColor(str);
        }

        void SetTransform(SourceOperation operation, SourceOperator op)
        {
            if (!desc.Position.IsDefault)
            {
                _logger.LogDebug(
                    "Setting transform for operation: {Operation}, operator: {Operator}, position: {Position}",
                    operation, op, desc.Position);
                if (op.Properties.FirstOrDefault(v => v.PropertyType == typeof(Transform)) is
                    IPropertyAdapter<Transform?> transformp)
                {
                    Transform? transform = transformp.GetValue();
                    AddOrSetHelper.AddOrSet(
                        ref transform,
                        new TranslateTransform(desc.Position),
                        [operation],
                        CommandRecorder);
                    transformp.SetValue(transform);
                }
                else
                {
                    _logger.LogWarning("The operator does not have a transform property.");
                }
            }
        }

        TimelineViewModel? timeline = FindToolTab<TimelineViewModel>();

        if (desc.FileName != null)
        {
            _logger.LogInformation("Adding element from file: {FileName}", desc.FileName);
            (TimeRange Range, int ZIndex)? scrollPos = null;

            Element CreateElementFor<T>(out T t)
                where T : SourceOperator, new()
            {
                Element element = CreateElement();
                element.Name = Path.GetFileName(desc.FileName);
                SetAccentColor(element, typeof(T).FullName!);

                element.Operation.AddChild(t = new T()).Do();
                SetTransform(element.Operation, t);

                return element;
            }

            var list = new List<IRecordableCommand>();
            if (MatchFileImage(desc.FileName))
            {
                _logger.LogDebug("File is an image.");
                Element element = CreateElementFor(out SourceImageOperator t);
                BitmapSource.TryOpen(desc.FileName, out BitmapSource? image);
                t.Value.Source.CurrentValue = image;

                CoreSerializer.StoreToUri(element, element.Uri!);
                list.Add(Scene.AddChild(element));
                scrollPos = (element.Range, element.ZIndex);
            }
            else if (MatchFileVideoOnly(desc.FileName))
            {
                _logger.LogDebug("File is a video.");
                Element element1 = CreateElementFor(out SourceVideoOperator t1);
                Element element2 = CreateElementFor(out SourceSoundOperator t2);
                element2.ZIndex++;
                VideoSource.TryOpen(desc.FileName, out VideoSource? video);
                t1.Value.Source.CurrentValue = video;
                if (video != null)
                    element1.Length = video.Duration;

                SoundSource.TryOpen(desc.FileName, out SoundSource? sound);
                t2.Value.Source.CurrentValue = sound;
                if (sound != null)
                    element2.Length = sound.Duration;

                CoreSerializer.StoreToUri(element1, element1.Uri!);
                CoreSerializer.StoreToUri(element2, element2.Uri!);
                list.Add(Scene.AddChild(element1));
                list.Add(Scene.AddChild(element2));
                // グループ化
                list.Add(Scene.Groups.BeginRecord<ImmutableHashSet<Guid>>()
                    .Add([element1.Id, element2.Id])
                    .ToCommand([Scene]));
                scrollPos = (element1.Range, element1.ZIndex);
            }
            else if (MatchFileAudioOnly(desc.FileName))
            {
                _logger.LogDebug("File is an audio.");
                Element element = CreateElementFor(out SourceSoundOperator t);
                SoundSource.TryOpen(desc.FileName, out SoundSource? sound);
                t.Value.Source.CurrentValue = sound;
                if (sound != null)
                {
                    element.Length = sound.Duration;
                }

                CoreSerializer.StoreToUri(element, element.Uri!);
                list.Add(Scene.AddChild(element));
                scrollPos = (element.Range, element.ZIndex);
            }

            list.ToArray()
                .ToCommand()
                .DoAndRecord(CommandRecorder);

            if (scrollPos.HasValue && timeline != null)
            {
                _logger.LogDebug("Scrolling to position: {ScrollPosition}", scrollPos.Value);
                timeline?.ScrollTo.Execute(scrollPos.Value);
            }
        }
        else
        {
            _logger.LogInformation("Adding new element without file.");
            Element element = CreateElement();
            if (desc.InitialOperator != null)
            {
                LibraryItem? item = LibraryService.Current.FindItem(desc.InitialOperator);
                if (item != null)
                {
                    element.Name = item.DisplayName;
                }

                //Todo: レイヤーのアクセントカラー
                //sLayer.AccentColor = item.InitialOperator.AccentColor;
                element.AccentColor =
                    ColorGenerator.GenerateColor(desc.InitialOperator.FullName ?? desc.InitialOperator.Name);
                var operatour = (SourceOperator)Activator.CreateInstance(desc.InitialOperator)!;
                element.Operation.AddChild(operatour).Do();
                SetTransform(element.Operation, operatour);
            }

            CoreSerializer.StoreToUri(element, element.Uri!);
            Scene.AddChild(element).DoAndRecord(CommandRecorder);

            timeline?.ScrollTo.Execute((element.Range, element.ZIndex));
        }

        _logger.LogInformation("Element added successfully.");
    }

    private static bool MatchFileExtensions(string filePath, IEnumerable<string> extensions)
    {
        string ext = Path.GetExtension(filePath);
        return extensions
            .Select(x =>
            {
                int idx = x.LastIndexOf('.');
                if (0 <= idx)
                    return x.Substring(idx);
                else
                    return x;
            })
            .Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    private static bool MatchFileAudioOnly(string filePath)
    {
        return MatchFileExtensions(filePath, DecoderRegistry.EnumerateDecoder()
            .SelectMany(x => x.AudioExtensions())
            .Distinct());
    }

    private static bool MatchFileVideoOnly(string filePath)
    {
        return MatchFileExtensions(filePath, DecoderRegistry.EnumerateDecoder()
            .SelectMany(x => x.VideoExtensions())
            .Distinct());
    }

    private static bool MatchFileImage(string filePath)
    {
        string[] extensions =
        [
            "*.bmp",
            "*.gif",
            "*.ico",
            "*.jpg",
            "*.jpeg",
            "*.png",
            "*.wbmp",
            "*.webp",
            "*.pkm",
            "*.ktx",
            "*.astc",
            "*.dng",
            "*.heif",
            "*.avif",
        ];
        return MatchFileExtensions(filePath, extensions);
    }

    void ISupportCloseAnimation.Close(object obj)
    {
        _logger.LogInformation("Closing animations related to object ({ObjectId}).", obj);

        var searcher = new ObjectSearcher(obj, v => v is IAnimation);

        IAnimation[] animations = searcher.SearchAll().OfType<IAnimation>().ToArray();
        TimelineViewModel? timeline = FindToolTab<TimelineViewModel>();

        // Timelineのインライン表示を削除
        if (timeline != null)
        {
            foreach (InlineAnimationLayerViewModel? item in timeline.Inlines
                         .IntersectBy(animations, v => v.Property.Animation)
                         .ToArray())
            {
                timeline.DetachInline(item);
                _logger.LogInformation("Detached inline animation ({AnimationId}) from timeline.",
                    item.Property.Animation);
            }
        }

        // BottomTabItemsから削除する
        foreach (var list in DockHost.GetNestedTools())
        {
            for (int index = list.Count - 1; index >= 0; index--)
            {
                ToolTabViewModel item = list[index];
                if (item.Context is not GraphEditorTabViewModel graph) continue;

                for (int i = graph.Items.Count - 1; i >= 0; i--)
                {
                    var animation = graph.Items[i];
                    if (animations.Contains(animation.Object))
                    {
                        graph.Items.Remove(animation);
                        _logger.LogInformation("Removed animation ({AnimationId}) from graph editor.", animation);
                    }
                }

                if (graph.Items.Count == 0)
                {
                    list.Remove(item);
                    item.Dispose();
                    _logger.LogInformation("Disposed empty graph editor tab.");
                }
            }
        }
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
            viewModel.CommandRecorder.Undo();
            viewModel._logger.LogInformation("Undo completed.");

            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> OnRedo()
        {
            viewModel._logger.LogInformation("Redoing last undone command.");
            viewModel.CommandRecorder.Redo();
            viewModel._logger.LogInformation("Redo completed.");

            return ValueTask.FromResult(true);
        }
    }
}
