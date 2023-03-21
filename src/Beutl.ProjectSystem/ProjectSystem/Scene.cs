using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Beutl.Framework;
using Beutl.Language;
using Beutl.Media;
using Beutl.Rendering;

using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Beutl.ProjectSystem;

public class Scene : ProjectItem, IHierarchicalRoot
{
    public static readonly CoreProperty<int> WidthProperty;
    public static readonly CoreProperty<int> HeightProperty;
    public static readonly CoreProperty<Layers> ChildrenProperty;
    public static readonly CoreProperty<TimeSpan> DurationProperty;
    public static readonly CoreProperty<TimeSpan> CurrentFrameProperty;
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
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .Register();

        HeightProperty = ConfigureProperty<int, Scene>(nameof(Height))
            .Accessor(o => o.Height)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .Register();

        ChildrenProperty = ConfigureProperty<Layers, Scene>(nameof(Children))
            .Accessor(o => o.Children, (o, v) => o.Children = v)
            .Register();

        DurationProperty = ConfigureProperty<TimeSpan, Scene>(nameof(Duration))
            .Accessor(o => o.Duration, (o, v) => o.Duration = v)
            .Display(Strings.DurationTime)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .SerializeName("duration")
            .Register();

        CurrentFrameProperty = ConfigureProperty<TimeSpan, Scene>(nameof(CurrentFrame))
            .Accessor(o => o.CurrentFrame, (o, v) => o.CurrentFrame = v)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .SerializeName("currentFrame")
            .Register();

        PreviewOptionsProperty = ConfigureProperty<PreviewOptions?, Scene>(nameof(PreviewOptions))
            .Accessor(o => o.PreviewOptions, (o, v) => o.PreviewOptions = v)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .Register();

        RendererProperty = ConfigureProperty<IRenderer, Scene>(nameof(Renderer))
            .Accessor(o => o.Renderer, (o, v) => o.Renderer = v)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .Register();

        NameProperty.OverrideMetadata<Scene>(new CorePropertyMetadata<string>(serializeName: "name"));

        CurrentFrameProperty.Changed.Subscribe(e =>
        {
            if (e.Sender is Scene scene)
            {
                scene._renderer.Invalidate(e.NewValue);
            }
        });
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

    [MemberNotNull("_renderer")]
    public void Initialize(int width, int height)
    {
        PixelSize oldSize = _renderer?.Graphics?.Size ?? PixelSize.Empty;
        _renderer?.Dispose();
        Renderer = new SceneRenderer(this, width, height);
        _renderer = Renderer;

        foreach (Layer item in _children.GetMarshal().Value)
        {
            IRenderLayer? context = _renderer[item.ZIndex];
            if (context == null)
            {
                context = new RenderLayer();
                _renderer[item.ZIndex] = context;
            }
            context.AddSpan(item.Span);
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

#pragma warning disable CA1822
    public IRecordableCommand MoveChild(int layerNum, TimeSpan start, TimeSpan length, Layer layer)
#pragma warning restore CA1822
    {
        ArgumentNullException.ThrowIfNull(layer);

        if (start < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(start));

        if (length <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(length));

        return new MoveCommand(layerNum, layer, start, layer.Start, length, layer.Length);
    }

    public IRecordableCommand MoveChildren(int deltaIndex, TimeSpan deltaStart, Layer[] layers)
    {
        if (layers.Length < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(layers));
        }

        return new MultipleMoveCommand(this, layers, deltaIndex, deltaStart);
    }

    public override void ReadFromJson(JsonNode json)
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

        base.ReadFromJson(json);

        if (json is JsonObject jobject)
        {
            if (jobject.TryGetPropertyValue("width", out JsonNode? widthNode)
                && jobject.TryGetPropertyValue("height", out JsonNode? heightNode)
                && widthNode != null
                && heightNode != null
                && widthNode.AsValue().TryGetValue(out int width)
                && heightNode.AsValue().TryGetValue(out int height))
            {
                Initialize(width, height);
            }

            if (jobject.TryGetPropertyValue("layers", out JsonNode? layersNode)
                && layersNode is JsonObject layersJson)
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

    public override void WriteToJson(ref JsonNode json)
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

        base.WriteToJson(ref json);
        if (_renderer != null)
        {
            json["width"] = _renderer.Graphics.Size.Width;
            json["height"] = _renderer.Graphics.Size.Height;
        }

        var layersNode = new JsonObject();

        UpdateInclude();

        Process(layersNode, "include", _includeLayers);
        Process(layersNode, "exclude", _excludeLayers);

        json["layers"] = layersNode;
    }

    protected override void SaveCore(string filename)
    {
        string? directory = Path.GetDirectoryName(filename);

        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        this.JsonSave(filename);
    }

    protected override void RestoreCore(string filename)
    {
        this.JsonRestore(filename);
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
        foreach (Layer item in Children.GetMarshal().Value)
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

                if (!_excludeLayers.Contains(rel) && File.Exists(item.FileName))
                {
                    _excludeLayers.Add(rel);
                }
            }
        }
    }

    private int NearestLayerNumber(Layer layer)
    {
        if (Children.Any(i => !(i.ZIndex != layer.ZIndex
            || i.Range.Intersects(layer.Range)
            || i.Range.Contains(layer.Range)
            || layer.Range.Contains(i.Range))))
        {
            int layerMax = Children.Max(i => i.ZIndex);

            // 使うことができるレイヤー番号
            var numbers = new List<int>();

            for (int l = 0; l <= layerMax; l++)
            {
                if (Children.Any(i => !(i.ZIndex != l
                    || i.Range.Intersects(layer.Range)
                    || i.Range.Contains(layer.Range)
                    || layer.Range.Contains(i.Range))))
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
            _scene.Children.Remove(_layer);
            _layer.ZIndex = -1;
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
            _scene.Children.Remove(_layer);
            _layer.ZIndex = -1;
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
        private readonly Layer _layer;
        private readonly int _layerNum;
        private readonly int _oldLayerNum;
        private readonly TimeSpan _newStart;
        private readonly TimeSpan _oldStart;
        private readonly TimeSpan _newLength;
        private readonly TimeSpan _oldLength;

        public MoveCommand(
            int layerNum,
            Layer layer,
            TimeSpan newStart, TimeSpan oldStart,
            TimeSpan newLength, TimeSpan oldLength)
        {
            _layer = layer;
            _layerNum = layerNum;
            _oldLayerNum = layer.ZIndex;
            _newStart = newStart;
            _oldStart = oldStart;
            _newLength = newLength;
            _oldLength = oldLength;
        }

        public void Do()
        {
            TimeSpan newEnd = _newStart + _newLength;
            (Layer? before, Layer? after, Layer? cover) = _layer.GetBeforeAndAfterAndCover(_layerNum, _newStart, newEnd);

            if (before != null && before.Range.End >= _newStart)
            {
                if ((after != null && (after.Start - before.Range.End) >= _newLength) || after == null)
                {
                    _layer.Start = before.Range.End;
                    _layer.Length = _newLength;
                    _layer.ZIndex = _layerNum;
                }
                else
                {
                    Undo();
                }
            }
            else if (after != null && after.Start < newEnd)
            {
                TimeSpan ns = after.Start - _newLength;
                if (((before != null && (after.Start - before.Range.End) >= _newLength) || before == null) && ns >= TimeSpan.Zero)
                {
                    _layer.Start = ns;
                    _layer.Length = _newLength;
                    _layer.ZIndex = _layerNum;
                }
                else
                {
                    Undo();
                }
            }
            else if (cover != null)
            {
                Undo();
            }
            else
            {
                _layer.Start = _newStart;
                _layer.Length = _newLength;
                _layer.ZIndex = _layerNum;
            }
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _layer.ZIndex = _oldLayerNum;
            _layer.Start = _oldStart;
            _layer.Length = _oldLength;
        }
    }

    private sealed class MultipleMoveCommand : IRecordableCommand
    {
        private readonly Layer[] _layers;
        private readonly int _deltaLayer;
        private readonly TimeSpan _deltaTime;
        private readonly bool _conflict;

        public MultipleMoveCommand(
            Scene scene,
            Layer[] layers,
            int deltaLayer,
            TimeSpan deltaTime)
        {
            _layers = layers;
            _deltaLayer = deltaLayer;
            _deltaTime = deltaTime;

            foreach (Layer item in layers)
            {
                _conflict = HasConflict(scene, _deltaLayer, _deltaTime);
                if (!_conflict)
                {
                    break;
                }
                else
                {
                    TimeSpan? newDeltaStart = DeltaStart(item);
                    if (newDeltaStart.HasValue)
                    {
                        _deltaTime = newDeltaStart.Value;
                    }
                }
            }

            _conflict = HasConflict(scene, _deltaLayer, _deltaTime);
        }

        private bool HasConflict(Scene scene, int deltaLayer, TimeSpan deltaTime)
        {
            Layer[] others = scene.Children.Except(_layers).ToArray();
            foreach (Layer item in _layers)
            {
                TimeRange newRange = item.Range.AddStart(deltaTime);
                int newLayer = item.ZIndex + deltaLayer;
                if (newLayer < 0 || newRange.Start.Ticks < 0)
                    return true;

                foreach (Layer other in others)
                {
                    if (other.ZIndex == newLayer && other.Range.Intersects(newRange))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private TimeSpan? DeltaStart(Layer layer)
        {
            TimeSpan newStart = layer.Start + _deltaTime;

            TimeSpan newEnd = newStart + layer.Length;
            int newIndex = layer.ZIndex + _deltaLayer;
            (Layer? before, Layer? after, Layer? _) = layer.GetBeforeAndAfterAndCover(newIndex, newStart, _layers);

            if (before != null && before.Range.End >= newStart)
            {
                if ((after != null && (after.Start - before.Range.End) >= layer.Length) || after == null)
                {
                    return before.Range.End - layer.Start;
                }
            }
            else if (after != null && after.Start < newEnd)
            {
                TimeSpan ns = after.Start - layer.Length;
                if (((before != null && (after.Start - before.Range.End) >= layer.Length) || before == null) && ns >= TimeSpan.Zero)
                {
                    return ns - layer.Start;
                }
            }
            else if (newStart.Ticks < 0)
            {
                return -layer.Start;
            }

            return null;
        }

        public void Do()
        {
            if (!_conflict)
            {
                foreach (Layer item in _layers)
                {
                    item.Start += _deltaTime;
                    item.ZIndex += _deltaLayer;
                }
            }
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            if (!_conflict)
            {
                foreach (Layer item in _layers)
                {
                    item.Start -= _deltaTime;
                    item.ZIndex -= _deltaLayer;
                }
            }
        }
    }
}
