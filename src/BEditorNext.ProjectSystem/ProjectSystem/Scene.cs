using System;
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
    public static readonly PropertyDefine<int> CurrentFrameProperty;
    public static readonly PropertyDefine<SceneLayer?> SelectedItemProperty;
    public static readonly PropertyDefine<PreviewOptions?> PreviewOptionsProperty;
    public static readonly PropertyDefine<TimelineOptions> TimelineOptionsProperty;
    private string? _fileName;
    private TimeSpan _duration;
    private int _currentFrame;
    private SceneLayer? _selectedItem;
    private PreviewOptions? _previewOptions;
    private TimelineOptions _timelineOptions;
    private readonly List<string> _includeLayers = new()
    {
        "**/*.layer"
    };
    private readonly List<string> _excludeLayers = new();

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

        CurrentFrameProperty = RegisterProperty<int, Scene>(nameof(CurrentFrame), (owner, obj) => owner.CurrentFrame = obj, owner => owner.CurrentFrame)
            .NotifyPropertyChanged(true)
            .NotifyPropertyChanging(true)
            .JsonName("currentFrame");

        SelectedItemProperty = RegisterProperty<SceneLayer?, Scene>(nameof(SelectedItem), (owner, obj) => owner.SelectedItem = obj, owner => owner.SelectedItem)
            .NotifyPropertyChanged(true)
            .NotifyPropertyChanging(true);

        PreviewOptionsProperty = RegisterProperty<PreviewOptions?, Scene>(nameof(PreviewOptions), (owner, obj) => owner.PreviewOptions = obj, owner => owner.PreviewOptions)
            .NotifyPropertyChanged(true)
            .NotifyPropertyChanging(true);

        TimelineOptionsProperty = RegisterProperty<TimelineOptions, Scene>(nameof(TimelineOptions), (owner, obj) => owner.TimelineOptions = obj, owner => owner.TimelineOptions)
            .NotifyPropertyChanged(true)
            .NotifyPropertyChanging(true);
    }

    public event EventHandler<CurrentFrameChangedEventArgs> CurrentFrameChanged
    {
        add => EventManager.AddEventHandler(value);
        remove => EventManager.RemoveEventHandler(value);
    }

    public int Width => Renderer.Graphics.Size.Width;

    public int Height => Renderer.Graphics.Size.Height;

    public TimeSpan Duration
    {
        get => _duration;
        set => SetAndRaise(DurationProperty, ref _duration, value);
    }

    public int CurrentFrame
    {
        get => _currentFrame;
        set
        {
            int old = _currentFrame;
            if (SetAndRaise(CurrentFrameProperty, ref _currentFrame, value))
            {
                EventManager.HandleEvent(this, new CurrentFrameChangedEventArgs(old, value), nameof(CurrentFrameChanged));
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

    public IRenderer Renderer { get; private set; }

    public string FileName => _fileName ?? throw new Exception("The file name is not set.");

    public DateTime LastSavedTime { get; private set; }

    [MemberNotNull("Renderer")]
    public void Initialize(int width, int height)
    {
        Renderer?.Dispose();
        Renderer = new SceneRenderer(this, width, height);

        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
    }

    // layer.FileNameが既に設定されている状態
    public void AddChild(SceneLayer layer, CommandRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(layer);

        if (recorder == null)
        {
            InsertChild(layer);
        }
        else
        {
            recorder.Do(new AddCommand(this, layer));
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
            recorder.Do(new RemoveCommand(this, layer));
        }
    }

    public void InsertChild(int layerNum, SceneLayer layer, CommandRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(layer);

        if (recorder == null)
        {
            layer.Layer = layerNum;
            InsertChild(layer);
        }
        else
        {
            recorder.Do(new AddCommand(this, layer, layerNum));
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
                widthNode!.AsValue().TryGetValue(out int width) &&
                heightNode!.AsValue().TryGetValue(out int height))
            {
                Initialize(width, height);
            }

            if (jobject.TryGetPropertyValue("duration", out JsonNode? durationNode) &&
                durationNode!.AsValue().TryGetValue(out string? durationStr) &&
                TimeSpan.TryParse(durationStr, out TimeSpan duration))
            {
                Duration = duration;
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

            InsertChild(layer);
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

    private void InsertChild(SceneLayer layer)
    {
        SceneLayer[] array = Children.OfType<SceneLayer>().ToArray();

        for (int i = 0; i < array.Length - 1; i++)
        {
            SceneLayer current = array[i];
            int nextIdx = i + 1;
            SceneLayer next = array[nextIdx];

            int curLayer = current.Layer;
            int nextLayer = next.Layer;

            if (curLayer <= layer.Layer && layer.Layer <= nextLayer)
            {
                Children.Insert(nextIdx, layer);
                return;
            }
        }

        Children.Add(layer);
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
            _scene.InsertChild(_layer);
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
            _scene.InsertChild(_layer);
        }
    }
}
