using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using BeUtl.Media;
using BeUtl.Rendering;

using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace BeUtl.ProjectSystem;

public class Scene : Element, IStorable, IWorkspaceItem
{
    public static readonly CoreProperty<int> WidthProperty;
    public static readonly CoreProperty<int> HeightProperty;
    public static readonly CoreProperty<Layers> ChildrenProperty;
    public static readonly CoreProperty<TimeSpan> DurationProperty;
    public static readonly CoreProperty<TimeSpan> CurrentFrameProperty;
    public static readonly CoreProperty<Layer?> SelectedItemProperty;
    public static readonly CoreProperty<PreviewOptions?> PreviewOptionsProperty;
    public static readonly CoreProperty<IRenderer> RendererProperty;
    private readonly List<string> _includeLayers = new()
    {
        "**/*.layer"
    };
    private readonly List<string> _excludeLayers = new();
    private readonly Layers _children;
    private string? _fileName;
    private TimeSpan _duration = TimeSpan.FromMinutes(5);
    private TimeSpan _currentFrame;
    private Layer? _selectedItem;
    private PreviewOptions? _previewOptions;
    private IRenderer _renderer;
    private EventHandler? _saved;
    private EventHandler? _restored;

    public Scene()
        : this(1920, 1080, string.Empty)
    {
    }

    public Scene(int width, int height, string name)
    {
        _children = new Layers(this);
        _children.CollectionChanged += Children_CollectionChanged;
        Initialize(width, height);
        Name = name;
    }

    static Scene()
    {
        WidthProperty = ConfigureProperty<int, Scene>(nameof(Width))
            .Accessor(o => o.Width)
            .Observability(PropertyObservability.Changed)
            .Register();

        HeightProperty = ConfigureProperty<int, Scene>(nameof(Height))
            .Accessor(o => o.Height)
            .Observability(PropertyObservability.Changed)
            .Register();

        ChildrenProperty = ConfigureProperty<Layers, Scene>(nameof(Children))
            .Accessor(o => o.Children, (o, v) => o.Children = v)
            .Register();

        DurationProperty = ConfigureProperty<TimeSpan, Scene>(nameof(Duration))
            .Accessor(o => o.Duration, (o, v) => o.Duration = v)
            .Observability(PropertyObservability.Changed)
            .SerializeName("duration")
            .Register();

        CurrentFrameProperty = ConfigureProperty<TimeSpan, Scene>(nameof(CurrentFrame))
            .Accessor(o => o.CurrentFrame, (o, v) => o.CurrentFrame = v)
            .Observability(PropertyObservability.Changed)
            .SerializeName("currentFrame")
            .Register();

        SelectedItemProperty = ConfigureProperty<Layer?, Scene>(nameof(SelectedItem))
            .Accessor(o => o.SelectedItem, (o, v) => o.SelectedItem = v)
            .Observability(PropertyObservability.DoNotNotifyLogicalTree)
            .Register();

        PreviewOptionsProperty = ConfigureProperty<PreviewOptions?, Scene>(nameof(PreviewOptions))
            .Accessor(o => o.PreviewOptions, (o, v) => o.PreviewOptions = v)
            .Observability(PropertyObservability.Changed)
            .Register();

        //TimelineOptionsProperty = ConfigureProperty<TimelineOptions, Scene>(nameof(TimelineOptions))
        //    .Accessor(o => o.TimelineOptions, (o, v) => o.TimelineOptions = v)
        //    .Observability(PropertyObservability.Changed)
        //    .Register();

        RendererProperty = ConfigureProperty<IRenderer, Scene>(nameof(Renderer))
            .Accessor(o => o.Renderer, (o, v) => o.Renderer = v)
            .Observability(PropertyObservability.Changed)
            .Register();

        NameProperty.OverrideMetadata<Scene>(new CorePropertyMetadata<string>
        {
            SerializeName = "name"
        });

        CurrentFrameProperty.Changed.Subscribe(e =>
        {
            if (e.Sender is Scene scene)
            {
                scene._renderer.Invalidate();
            }
        });
    }

    event EventHandler IStorable.Saved
    {
        add => _saved += value;
        remove => _saved -= value;
    }
    
    event EventHandler IStorable.Restored
    {
        add => _restored += value;
        remove => _restored -= value;
    }

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

            SetAndRaise(CurrentFrameProperty, ref _currentFrame, value);
        }
    }

    public Layers Children
    {
        get => _children;
        set => _children.Replace(value);
    }

    public Layer? SelectedItem
    {
        get => _selectedItem;
        set => SetAndRaise(SelectedItemProperty, ref _selectedItem, value);
    }

    public PreviewOptions? PreviewOptions
    {
        get => _previewOptions;
        set => SetAndRaise(PreviewOptionsProperty, ref _previewOptions, value);
    }

    public IRenderer Renderer
    {
        get => _renderer;
        private set => SetAndRaise(RendererProperty, ref _renderer, value);
    }

    public string FileName => _fileName ?? throw new Exception("The file name is not set.");

    public DateTime LastSavedTime { get; private set; }

    [MemberNotNull("_renderer")]
    public void Initialize(int width, int height)
    {
        PixelSize oldSize = _renderer?.Graphics?.Size ?? PixelSize.Empty;
        _renderer?.Dispose();
        Renderer = new SceneRenderer(this, width, height);
        _renderer = Renderer;

        foreach (Layer item in _children.AsSpan())
        {
            _renderer[item.ZIndex] = item.Renderable;
        }

        OnPropertyChanged(new CorePropertyChangedEventArgs<int>(
            sender: this,
            property: WidthProperty,
            metadata: WidthProperty.GetMetadata<Scene, CorePropertyMetadata>(),
            newValue: width,
            oldValue: oldSize.Width));

        OnPropertyChanged(new CorePropertyChangedEventArgs<int>(
            sender: this,
            property: HeightProperty,
            metadata: HeightProperty.GetMetadata<Scene, CorePropertyMetadata>(),
            newValue: height,
            oldValue: oldSize.Height));
    }

    // layer.FileNameが既に設定されている状態
    public IRecordableCommand AddChild(Layer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        layer.ZIndex = NearestLayerNumber(layer);

        return new AddCommand(this, layer);
    }

    public IRecordableCommand RemoveChild(Layer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);

        return new RemoveCommand(this, layer);
    }

    public IRecordableCommand MoveChild(int layerNum, Layer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);

        return new MoveCommand(layerNum, this, layer);
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

        this.JsonSave(filename);

        _saved?.Invoke(this, EventArgs.Empty);
    }

    public void Restore(string filename)
    {
        _fileName = filename;
        LastSavedTime = DateTime.Now;

        this.JsonRestore(filename);

        _restored?.Invoke(this, EventArgs.Empty);
    }

    private void SyncronizeLayers(IEnumerable<string> pathToLayer)
    {
        string baseDir = Path.GetDirectoryName(FileName)!;
        pathToLayer = pathToLayer.Select(x => Path.GetFullPath(x, baseDir)).ToArray();

        // 削除するLayers
        IEnumerable<Layer> toRemoveLayers = Children.ExceptBy(pathToLayer, x => x.FileName);
        // 追加するLayers
        IEnumerable<string> toAddLayers = pathToLayer.Except(Children.Select(x => x.FileName));

        foreach (Layer item in toRemoveLayers)
        {
            Children.Remove(item);
        }

        foreach (string item in toAddLayers)
        {
            var layer = new Layer();
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
        foreach (Layer item in Children.AsSpan())
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
            string dirPath = Path.GetDirectoryName(FileName)!;
            foreach (Layer item in e.OldItems.OfType<Layer>())
            {
                string rel = Path.GetRelativePath(dirPath, item.FileName).Replace('\\', '/');

                if (!_excludeLayers.Contains(rel))
                {
                    _excludeLayers.Add(rel);
                }
            }
        }
    }

    private int NearestLayerNumber(Layer layer)
    {
        if (Children.Select(i => i.ZIndex).Contains(layer.ZIndex))
        {
            int layerMax = Children.Max(i => i.ZIndex);

            // 使われていないレイヤー番号
            var numbers = new List<int>();

            for (int l = 0; l <= layerMax; l++)
            {
                if (!Children.Select(i => i.ZIndex).Contains(l))
                {
                    numbers.Add(l);
                }
            }

            if (numbers.Count < 1)
            {
                return layerMax + 1;
            }

            return numbers.Nearest(layer.ZIndex);
        }

        return layer.ZIndex;
    }

    private sealed class AddCommand : IRecordableCommand
    {
        private readonly Scene _scene;
        private readonly Layer _layer;
        private readonly int _layerNum;

        public AddCommand(Scene scene, Layer layer)
        {
            _scene = scene;
            _layer = layer;
            _layerNum = layer.ZIndex;
        }

        public AddCommand(Scene scene, Layer layer, int layerNum)
        {
            _scene = scene;
            _layer = layer;
            _layerNum = layerNum;
        }

        public void Do()
        {
            _layer.ZIndex = _layerNum;
            _scene.Children.Add(_layer);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _layer.ZIndex = -1;
            _scene.Children.Remove(_layer);
        }
    }

    private sealed class RemoveCommand : IRecordableCommand
    {
        private readonly Scene _scene;
        private readonly Layer _layer;
        private int _layerNum;

        public RemoveCommand(Scene scene, Layer layer)
        {
            _scene = scene;
            _layer = layer;
        }

        public void Do()
        {
            _layerNum = _layer.ZIndex;
            _layer.ZIndex = -1;
            _scene.Children.Remove(_layer);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _layer.ZIndex = _layerNum;
            _scene.Children.Add(_layer);
        }
    }

    private sealed class MoveCommand : IRecordableCommand
    {
        private readonly Scene _scene;
        private readonly Layer _layer;
        private readonly int _layerNum;
        private IRecordableCommand? _inner;

        public MoveCommand(int layerNum, Scene scene, Layer layer)
        {
            _scene = scene;
            _layer = layer;
            _layerNum = layerNum;
        }

        public void Do()
        {
            if (_inner != null)
            {
                Redo();
            }

            using var tracker = new PropertyChangeTracker(_scene.Children, 0);
            Span<Layer> span = _scene.Children.AsSpan();

            // 下に移動
            if (_layerNum > _layer.ZIndex)
            {
                bool insert = false;
                foreach (Layer item in span)
                {
                    if (item.ZIndex == _layerNum)
                    {
                        insert = true;
                    }
                }

                if (insert)
                {
                    foreach (Layer item in span)
                    {
                        if (item != _layer)
                        {
                            if (item.ZIndex > _layer.ZIndex &&
                                item.ZIndex <= _layerNum)
                            {
                                item.ZIndex--;
                            }
                        }
                    }
                }
            }
            else if (_layerNum < _layer.ZIndex)
            {
                bool insert = false;
                foreach (Layer item in span)
                {
                    if (item.ZIndex == _layerNum)
                    {
                        insert = true;
                    }
                }

                if (insert)
                {
                    foreach (Layer item in span)
                    {
                        if (item != _layer)
                        {
                            if (item.ZIndex < _layer.ZIndex &&
                                item.ZIndex >= _layerNum)
                            {
                                item.ZIndex++;
                            }
                        }
                    }
                }
            }

            _layer.ZIndex = _layerNum;

            _inner = tracker.ToCommand();
        }

        public void Redo()
        {
            if (_inner == null)
                throw new InvalidOperationException();
            _inner.Redo();
        }

        public void Undo()
        {
            if (_inner == null)
                throw new InvalidOperationException();
            _inner.Undo();
        }
    }
}
