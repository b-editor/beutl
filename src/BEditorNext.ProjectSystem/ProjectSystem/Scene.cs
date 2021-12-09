using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

using BEditorNext.Rendering;

using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace BEditorNext.ProjectSystem;

public class Scene : Element, IStorable
{
    public static readonly PropertyDefine<int> WidthProperty;
    public static readonly PropertyDefine<int> HeightProperty;
    public static readonly PropertyDefine<TimeSpan> DurationProperty;
    public static readonly PropertyDefine<TimeSpan> CurrentFrameProperty;
    public static readonly PropertyDefine<SceneLayer?> SelectedItemProperty;
    public static readonly PropertyDefine<PreviewOptions?> PreviewOptionsProperty;
    public static readonly PropertyDefine<TimelineOptions> TimelineOptionsProperty;
    private readonly List<string> _includeLayers = new()
    {
        "**/*.layer"
    };
    private readonly List<string> _excludeLayers = new();
    private string? _fileName;
    private TimeSpan _duration = TimeSpan.FromMinutes(5);
    private TimeSpan _currentFrame;
    private SceneLayer? _selectedItem;
    private PreviewOptions? _previewOptions;
    private TimelineOptions _timelineOptions = new();
    private SceneRenderer _renderer;

    public Scene()
        : this(1920, 1080, string.Empty)
    {
    }

    public Scene(int width, int height, string name)
    {
        Initialize(width, height);
        Name = name;
        Children.CollectionChanged += Children_CollectionChanged;
    }

    static Scene()
    {
        WidthProperty = RegisterProperty<int, Scene>(nameof(Width), owner => owner.Width)
            .NotifyPropertyChanged(true)
            .JsonName("width");

        HeightProperty = RegisterProperty<int, Scene>(nameof(Height), owner => owner.Height)
            .NotifyPropertyChanged(true)
            .JsonName("height");

        DurationProperty = RegisterProperty<TimeSpan, Scene>(nameof(Duration), (owner, obj) => owner.Duration = obj, owner => owner.Duration)
            .NotifyPropertyChanged(true)
            .NotifyPropertyChanging(true);

        CurrentFrameProperty = RegisterProperty<TimeSpan, Scene>(nameof(CurrentFrame), (owner, obj) => owner.CurrentFrame = obj, owner => owner.CurrentFrame)
            .NotifyPropertyChanged(true)
            .NotifyPropertyChanging(true);

        SelectedItemProperty = RegisterProperty<SceneLayer?, Scene>(nameof(SelectedItem), (owner, obj) => owner.SelectedItem = obj, owner => owner.SelectedItem)
            .NotifyPropertyChanged(true)
            .NotifyPropertyChanging(true);

        PreviewOptionsProperty = RegisterProperty<PreviewOptions?, Scene>(nameof(PreviewOptions), (owner, obj) => owner.PreviewOptions = obj, owner => owner.PreviewOptions)
            .NotifyPropertyChanged(true)
            .NotifyPropertyChanging(true);

        TimelineOptionsProperty = RegisterProperty<TimelineOptions, Scene>(nameof(TimelineOptions), (owner, obj) => owner.TimelineOptions = obj, owner => owner.TimelineOptions)
            .NotifyPropertyChanged(true)
            .NotifyPropertyChanging(true);

        CurrentFrameProperty.Changed.Subscribe(e =>
        {
            if (e.Sender is Scene scene)
            {
                scene._renderer.ForceRender();
            }
        });
    }

    public event EventHandler<CurrentFrameChangedEventArgs>? CurrentFrameChanged;

    public int Width => Renderer.Graphics.Size.Width;

    public int Height => Renderer.Graphics.Size.Height;

    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            if (value < TimeSpan.Zero)
                value = TimeSpan.Zero;

            SetAndRaise(DurationProperty, ref _duration, value);
        }
    }

    public TimeSpan CurrentFrame
    {
        get => _currentFrame;
        set
        {
            if (value < TimeSpan.Zero)
                value = TimeSpan.Zero;
            if (value > Duration)
                value = Duration;

            TimeSpan old = _currentFrame;
            if (SetAndRaise(CurrentFrameProperty, ref _currentFrame, value))
            {
                CurrentFrameChanged?.Invoke(this, new CurrentFrameChangedEventArgs(old, value));
            }
        }
    }

    public IEnumerable<SceneLayer> Layers => Children.OfType<SceneLayer>();

    public SceneLayer? SelectedItem
    {
        get => _selectedItem;
        set => SetAndRaise(SelectedItemProperty, ref _selectedItem, value);
    }

    public PreviewOptions? PreviewOptions
    {
        get => _previewOptions;
        set => SetAndRaise(PreviewOptionsProperty, ref _previewOptions, value);
    }

    public TimelineOptions TimelineOptions
    {
        get => _timelineOptions;
        set => SetAndRaise(TimelineOptionsProperty, ref _timelineOptions, value);
    }

    public IRenderer Renderer => _renderer;

    public string FileName => _fileName ?? throw new Exception("The file name is not set.");

    public DateTime LastSavedTime { get; private set; }

    [MemberNotNull("_renderer")]
    public void Initialize(int width, int height)
    {
        _renderer?.Dispose();
        _renderer = new SceneRenderer(this, width, height);

        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
    }

    // layer.FileNameが既に設定されている状態
    public void AddChild(SceneLayer layer, CommandRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(layer);

        if (recorder == null)
        {
            Children.Add(layer);
        }
        else
        {
            recorder.DoAndPush(new AddCommand(this, layer));
        }
    }

    public void RemoveChild(SceneLayer layer, CommandRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(layer);

        if (recorder == null)
        {
            layer.Layer = -1;
            Children.Remove(layer);
        }
        else
        {
            recorder.DoAndPush(new RemoveCommand(this, layer));
        }
    }

    public void MoveChild(int layerNum, SceneLayer layer, CommandRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(layer);

        PropertyChangeTracker? tracker = recorder != null ? new PropertyChangeTracker(Layers, 0) : null;

        // 下に移動
        if (layerNum > layer.Layer)
        {
            bool insert = false;
            foreach (SceneLayer item in Layers)
            {
                if (item.Layer == layerNum)
                {
                    insert = true;
                }
            }

            if (insert)
            {
                foreach (SceneLayer item in Layers)
                {
                    if (item != layer)
                    {
                        if (item.Layer > layer.Layer &&
                            item.Layer <= layerNum)
                        {
                            item.Layer--;
                        }
                    }
                }
            }
        }
        else if (layerNum < layer.Layer)
        {
            bool insert = false;
            foreach (SceneLayer item in Layers)
            {
                if (item.Layer == layerNum)
                {
                    insert = true;
                }
            }

            if (insert)
            {
                foreach (SceneLayer item in Layers)
                {
                    if (item != layer)
                    {
                        if (item.Layer < layer.Layer &&
                            item.Layer >= layerNum)
                        {
                            item.Layer++;
                        }
                    }
                }
            }
        }

        layer.Layer = layerNum;

        if (tracker != null && recorder != null)
        {
            IRecordableCommand command = tracker.ToCommand();
            tracker.Dispose();
            recorder.PushOnly(command);
        }
    }

    public override void FromJson(JsonNode json)
    {
        static void Process(Func<string, Matcher> add, JsonNode node, List<string> list)
        {
            list.Clear();
            if (node is JsonValue jvalue &&
                jvalue.TryGetValue(out string? pattern))
            {
                list.Add(pattern);
                add(pattern);
            }
            else if (node is JsonArray array)
            {
                foreach (JsonValue item in array.OfType<JsonValue>())
                {
                    if (item.TryGetValue(out pattern))
                    {
                        list.Add(pattern);
                        add(pattern);
                    }
                }
            }
        }

        base.FromJson(json);

        if (json is JsonObject jobject)
        {
            if (jobject.TryGetPropertyValue("width", out JsonNode? widthNode) &&
                jobject.TryGetPropertyValue("height", out JsonNode? heightNode) &&
                widthNode != null &&
                heightNode != null &&
                widthNode.AsValue().TryGetValue(out int width) &&
                heightNode.AsValue().TryGetValue(out int height))
            {
                Initialize(width, height);
            }

            if (jobject.TryGetPropertyValue("duration", out JsonNode? durationNode) &&
                durationNode != null &&
                durationNode.AsValue().TryGetValue(out string? durationStr) &&
                TimeSpan.TryParse(durationStr, out TimeSpan duration))
            {
                Duration = duration;
            }

            if (jobject.TryGetPropertyValue("currentFrame", out JsonNode? currentFrameNode) &&
                currentFrameNode != null &&
                currentFrameNode.AsValue().TryGetValue(out string? currentFrameStr) &&
                TimeSpan.TryParse(currentFrameStr, out TimeSpan currentFrame))
            {
                CurrentFrame = currentFrame;
            }

            if (jobject.TryGetPropertyValue("layers", out JsonNode? layersNode) &&
                layersNode is JsonObject layersJson)
            {
                var matcher = new Matcher();
                var directory = new DirectoryInfoWrapper(new DirectoryInfo(Path.GetDirectoryName(FileName)!));

                // 含めるクリップ
                if (layersJson.TryGetPropertyValue("include", out JsonNode? includeNode))
                {
                    Process(matcher.AddInclude, includeNode!, _includeLayers);
                }

                // 除外するクリップ
                if (layersJson.TryGetPropertyValue("exclude", out JsonNode? excludeNode))
                {
                    Process(matcher.AddExclude, excludeNode!, _excludeLayers);
                }

                PatternMatchingResult result = matcher.Execute(directory);
                SyncronizeLayers(result.Files.Select(x => x.Path));
            }
            else
            {
                Children.Clear();
            }
        }
    }

    public override JsonNode ToJson()
    {
        static void Process(JsonObject jobject, string jsonName, List<string> list)
        {
            if (list.Count == 1)
            {
                jobject[jsonName] = JsonValue.Create(list[0]);
            }
            else if (list.Count >= 2)
            {
                var jarray = new JsonArray();
                foreach (string item in list)
                {
                    jarray.Add(JsonValue.Create(item));
                }

                jobject[jsonName] = jarray;
            }
            else
            {
                jobject.Remove(jsonName);
            }
        }

        JsonNode node = base.ToJson();

        if (node is JsonObject jobject)
        {
            var layersNode = new JsonObject();

            UpdateInclude();

            Process(layersNode, "include", _includeLayers);
            Process(layersNode, "exclude", _excludeLayers);

            jobject["layers"] = layersNode;
            jobject["duration"] = JsonValue.Create(Duration.ToString());
            jobject["currentFrame"] = JsonValue.Create(CurrentFrame.ToString());
        }

        return node;
    }

    public void Save(string filename)
    {
        _fileName = filename;
        LastSavedTime = DateTime.Now;
        string? directory = Path.GetDirectoryName(_fileName);

        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.Write);
        using var writer = new Utf8JsonWriter(stream, JsonHelper.WriterOptions);

        ToJson().WriteTo(writer, JsonHelper.SerializerOptions);
    }

    public void Restore(string filename)
    {
        _fileName = filename;
        LastSavedTime = DateTime.Now;

        using var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
        var node = JsonNode.Parse(stream);

        if (node != null)
        {
            FromJson(node);
        }
    }

    private void SyncronizeLayers(IEnumerable<string> pathToLayer)
    {
        string baseDir = Path.GetDirectoryName(FileName)!;
        pathToLayer = pathToLayer.Select(x => Path.GetFullPath(x, baseDir)).ToArray();

        // 削除するLayers
        IEnumerable<SceneLayer> toRemoveLayers = Layers.ExceptBy(pathToLayer, x => x.FileName);
        // 追加するLayers
        IEnumerable<string> toAddLayers = pathToLayer.Except(Layers.Select(x => x.FileName));

        foreach (SceneLayer item in toRemoveLayers)
        {
            Children.Remove(item);
        }

        foreach (string item in toAddLayers)
        {
            var layer = new SceneLayer();
            layer.Restore(item);

            Children.Add(layer);
        }
    }

    private void UpdateInclude()
    {
        string dirPath = Path.GetDirectoryName(FileName)!;
        var directory = new DirectoryInfoWrapper(new DirectoryInfo(dirPath));

        var matcher = new Matcher();
        matcher.AddIncludePatterns(_includeLayers);
        matcher.AddExcludePatterns(_excludeLayers);

        string[] files = matcher.Execute(directory).Files.Select(x => x.Path).ToArray();
        foreach (SceneLayer item in Layers)
        {
            string rel = Path.GetRelativePath(dirPath, item.FileName).Replace('\\', '/');

            // 含まれていない場合追加
            if (!files.Contains(rel))
            {
                _includeLayers.Add(rel);
            }
        }
    }

    private void Children_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Remove &&
            e.OldItems != null)
        {
            foreach (SceneLayer item in e.OldItems.OfType<SceneLayer>())
            {
                _excludeLayers.Add(item.FileName);
            }
        }
    }

    private sealed class AddCommand : IRecordableCommand
    {
        private readonly Scene _scene;
        private readonly SceneLayer _layer;
        private readonly int _layerNum;

        public AddCommand(Scene scene, SceneLayer layer)
        {
            _scene = scene;
            _layer = layer;
            _layerNum = layer.Layer;
        }

        public AddCommand(Scene scene, SceneLayer layer, int layerNum)
        {
            _scene = scene;
            _layer = layer;
            _layerNum = layerNum;
        }

        public void Do()
        {
            _layer.Layer = _layerNum;
            _scene.Children.Add(_layer);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _layer.Layer = -1;
            _scene.Children.Remove(_layer);
        }
    }

    private sealed class RemoveCommand : IRecordableCommand
    {
        private readonly Scene _scene;
        private readonly SceneLayer _layer;
        private int _layerNum;

        public RemoveCommand(Scene scene, SceneLayer layer)
        {
            _scene = scene;
            _layer = layer;
        }

        public void Do()
        {
            _layerNum = _layer.Layer;
            _layer.Layer = -1;
            _scene.Children.Remove(_layer);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _layer.Layer = _layerNum;
            _scene.Children.Add(_layer);
        }
    }
}
