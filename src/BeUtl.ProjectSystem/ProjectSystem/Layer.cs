using System.Text.Json;
using System.Text.Json.Nodes;

using BeUtl.Collections;
using BeUtl.Commands;
using BeUtl.Media;
using BeUtl.Rendering;

namespace BeUtl.ProjectSystem;

public class Layer : Element, IStorable, ILogicalElement
{
    public static readonly CoreProperty<TimeSpan> StartProperty;
    public static readonly CoreProperty<TimeSpan> LengthProperty;
    public static readonly CoreProperty<int> ZIndexProperty;
    public static readonly CoreProperty<Color> AccentColorProperty;
    public static readonly CoreProperty<bool> IsEnabledProperty;
    public static readonly CoreProperty<Renderable?> RenderableProperty;
    private TimeSpan _start;
    private TimeSpan _length;
    private int _zIndex;
    private string? _fileName;
    private bool _isEnabled = true;
    private Renderable? _renderable;
    private LogicalList<LayerOperation> _children;

    static Layer()
    {
        StartProperty = ConfigureProperty<TimeSpan, Layer>(nameof(Start))
            .Accessor(o => o.Start, (o, v) => o.Start = v)
            .Observability(PropertyObservability.Changed)
            .SerializeName("start")
            .Register();

        LengthProperty = ConfigureProperty<TimeSpan, Layer>(nameof(Length))
            .Accessor(o => o.Length, (o, v) => o.Length = v)
            .Observability(PropertyObservability.Changed)
            .SerializeName("length")
            .Register();

        ZIndexProperty = ConfigureProperty<int, Layer>(nameof(ZIndex))
            .Accessor(o => o.ZIndex, (o, v) => o.ZIndex = v)
            .Observability(PropertyObservability.Changed)
            .SerializeName("zIndex")
            .Register();

        AccentColorProperty = ConfigureProperty<Color, Layer>(nameof(AccentColor))
            .DefaultValue(Colors.Teal)
            .Observability(PropertyObservability.Changed)
            .SerializeName("accentColor")
            .Register();

        IsEnabledProperty = ConfigureProperty<bool, Layer>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .Observability(PropertyObservability.Changed)
            .SerializeName("isEnabled")
            .Register();

        RenderableProperty = ConfigureProperty<Renderable?, Layer>(nameof(Renderable))
            .Accessor(o => o.Renderable, (o, v) => o.Renderable = v)
            .Observability(PropertyObservability.Changed)
            .Register();

        NameProperty.OverrideMetadata<Layer>(new CorePropertyMetadata<string>
        {
            SerializeName = "name"
        });

        ZIndexProperty.Changed.Subscribe(args =>
        {
            if (args.Sender is Layer layer && layer.Parent is Scene { Renderer: { IsDisposed: false } renderer })
            {
                renderer[args.OldValue] = null;
                if (args.NewValue >= 0)
                {
                    renderer[args.NewValue] = layer.Renderable;
                }
            }
        });

        RenderableProperty.Changed.Subscribe(args =>
        {
            if (args.Sender is Layer layer && layer.Parent is Scene { Renderer: { IsDisposed: false } renderer } && layer.ZIndex >= 0)
            {
                renderer[layer.ZIndex] = args.NewValue;
            }
        });

        IsEnabledProperty.Changed.Subscribe(args =>
        {
            if (args.Sender is Layer layer)
            {
                layer.ForceRender();
            }
        });
    }

    public Layer()
    {
        _children = new LogicalList<LayerOperation>(this);
    }

    // 0以上
    public TimeSpan Start
    {
        get => _start;
        set => SetAndRaise(StartProperty, ref _start, value);
    }

    public TimeSpan Length
    {
        get => _length;
        set => SetAndRaise(LengthProperty, ref _length, value);
    }

    public int ZIndex
    {
        get => _zIndex;
        set => SetAndRaise(ZIndexProperty, ref _zIndex, value);
    }

    public Color AccentColor
    {
        get => GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetAndRaise(IsEnabledProperty, ref _isEnabled, value);
    }

    public Renderable? Renderable
    {
        get => _renderable;
        set => SetAndRaise(RenderableProperty, ref _renderable, value);
    }

    public string FileName
    {
        get => _fileName ?? throw new Exception("The file name is not set.");
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _fileName = value;
        }
    }

    public DateTime LastSavedTime { get; private set; }

    public LogicalList<LayerOperation> Children => _children;

    IEnumerable<ILogicalElement> ILogicalElement.LogicalChildren => Children;

    public IRecordableCommand UpdateTime(TimeSpan start, TimeSpan length)
    {
        if (start < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(start));

        if (length <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(length));

        return new UpdateTimeCommand(this, start, Start, length, Length);
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

    public override void FromJson(JsonNode json)
    {
        base.FromJson(json);

        if (json is JsonObject jobject)
        {
            // Todo: 後で削除
            if (!jobject.ContainsKey("zIndex") && jobject.TryGetPropertyValue("layer", out JsonNode? layerNode) &&
                layerNode is JsonValue layerValue &&
                layerValue.TryGetValue(out int layer))
            {
                ZIndex = layer;
            }

            if (jobject.TryGetPropertyValue("operations", out JsonNode? operationsNode) &&
                operationsNode is JsonArray operationsArray)
            {
                foreach (JsonObject operationJson in operationsArray.OfType<JsonObject>())
                {
                    if (operationJson.TryGetPropertyValue("@type", out JsonNode? atTypeNode) &&
                        atTypeNode is JsonValue atTypeValue &&
                        atTypeValue.TryGetValue(out string? atType))
                    {
                        var type = TypeFormat.ToType(atType);
                        LayerOperation? operation = null;

                        if (type?.IsAssignableTo(typeof(LayerOperation)) ?? false)
                        {
                            operation = Activator.CreateInstance(type) as LayerOperation;
                        }

                        operation ??= new EmptyOperation();
                        operation.FromJson(operationJson);
                        Children.Add(operation);
                    }
                }
            }
        }
    }

    public override JsonNode ToJson()
    {
        JsonNode node = base.ToJson();

        if (node is JsonObject jobject)
        {
            var array = new JsonArray();

            foreach (LayerOperation item in Children)
            {
                JsonNode json = item.ToJson();
                if (item is not EmptyOperation)
                {
                    json["@type"] = TypeFormat.ToString(item.GetType());
                }

                if (json.Parent != null)
                {
                    json = JsonNode.Parse(json.ToJsonString())!;
                }

                array.Add(json);
            }

            jobject["operations"] = array;
        }

        return node;
    }

    public IRecordableCommand AddChild(LayerOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return new AddCommand<LayerOperation>(Children, operation, Children.Count);
    }

    public IRecordableCommand RemoveChild(LayerOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return new RemoveCommand<LayerOperation>(Children, operation);
    }

    public IRecordableCommand InsertChild(int index, LayerOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return new AddCommand<LayerOperation>(Children, operation, index);
    }

    protected override void OnAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnAttachedToLogicalTree(args);
        if (args.Parent is Scene { Renderer: { IsDisposed: false } renderer } && ZIndex >= 0)
        {
            renderer[ZIndex] = Renderable;
        }
    }

    protected override void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnDetachedFromLogicalTree(args);
        if (args.Parent is Scene { Renderer: { IsDisposed: false } renderer } && ZIndex >= 0)
        {
            renderer[ZIndex] = null;
        }
    }

    internal bool InRange(TimeSpan ts)
    {
        return Start <= ts && ts < Length + Start;
    }

    private void ForceRender()
    {
        Scene? scene = this.FindLogicalParent<Scene>();
        if (scene != null &&
            Start <= scene.CurrentFrame &&
            scene.CurrentFrame < Start + Length &&
            scene.Renderer is { IsDisposed: false })
        {
            scene.Renderer.Invalidate();
        }
    }

    private sealed class UpdateTimeCommand : IRecordableCommand
    {
        private readonly Layer _layer;
        private readonly TimeSpan _newStart;
        private readonly TimeSpan _oldStart;
        private readonly TimeSpan _newLength;
        private readonly TimeSpan _oldLength;

        public UpdateTimeCommand(Layer layer, TimeSpan newStart, TimeSpan oldStart, TimeSpan newLength, TimeSpan oldLength)
        {
            _layer = layer;
            _newStart = newStart;
            _oldStart = oldStart;
            _newLength = newLength;
            _oldLength = oldLength;
        }

        public void Do()
        {
            _layer.Start = _newStart;
            _layer.Length = _newLength;
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _layer.Start = _oldStart;
            _layer.Length = _oldLength;
        }
    }
}
