using System.Collections.Specialized;
using System.Text.Json;
using System.Text.Json.Nodes;

using BeUtl.Commands;
using BeUtl.Media;
using BeUtl.Rendering;

namespace BeUtl.ProjectSystem;

public class Layer : Element, IStorable
{
    public static readonly CoreProperty<TimeSpan> StartProperty;
    public static readonly CoreProperty<TimeSpan> LengthProperty;
    public static readonly CoreProperty<int> ZIndexProperty;
    public static readonly CoreProperty<Color> AccentColorProperty;
    public static readonly CoreProperty<bool> IsEnabledProperty;
    private TimeSpan _start;
    private TimeSpan _length;
    private int _zIndex;
    private string? _fileName;
    private bool _isEnabled = true;

    static Layer()
    {
        StartProperty = ConfigureProperty<TimeSpan, Layer>(nameof(Start))
            .Accessor(o => o.Start, (o, v) => o.Start = v)
            .Observability(PropertyObservability.ChangingAndChanged)
            .JsonName("start")
            .Register();

        LengthProperty = ConfigureProperty<TimeSpan, Layer>(nameof(Length))
            .Accessor(o => o.Length, (o, v) => o.Length = v)
            .Observability(PropertyObservability.ChangingAndChanged)
            .JsonName("length")
            .Register();

        ZIndexProperty = ConfigureProperty<int, Layer>(nameof(ZIndex))
            .Accessor(o => o.ZIndex, (o, v) => o.ZIndex = v)
            .Observability(PropertyObservability.ChangingAndChanged)
            .JsonName("zIndex")
            .Register();

        AccentColorProperty = ConfigureProperty<Color, Layer>(nameof(AccentColor))
            .DefaultValue(Colors.Teal)
            .Observability(PropertyObservability.ChangingAndChanged)
            .JsonName("accentColor")
            .Register();

        IsEnabledProperty = ConfigureProperty<bool, Layer>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .Observability(PropertyObservability.ChangingAndChanged)
            .JsonName("isEnabled")
            .Register();

        NameProperty.OverrideMetadata(typeof(Layer), new CorePropertyMetadata(null, PropertyObservability.None, new()
        {
            { PropertyMetaTableKeys.JsonName, "name" }
        }));

        ZIndexProperty.Changed.Subscribe(args =>
        {
            if (args.Sender is Layer layer && layer.Parent is Scene { Renderer: { IsDisposed: false } renderer })
            {
                renderer[args.OldValue] = null;
                renderer[args.NewValue] = layer.Scope;
            }
        });
    }

    public Layer()
    {
        Children.CollectionChanged += Children_CollectionChanged;
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

    public ILayerScope Scope { get; } = new LayerScope();

    public IEnumerable<LayerOperation> Operations => Children.OfType<LayerOperation>();

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

    public void AddChild(LayerOperation operation, CommandRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (recorder == null)
        {
            Children.Add(operation);
        }
        else
        {
            recorder.DoAndPush(new AddCommand<Element>(Children, operation, Children.Count));
        }
    }

    public void RemoveChild(LayerOperation operation, CommandRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (recorder == null)
        {
            Children.Remove(operation);
        }
        else
        {
            recorder.DoAndPush(new RemoveCommand<Element>(Children, operation));
        }
    }

    public void InsertChild(int index, LayerOperation operation, CommandRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (recorder == null)
        {
            Children.Insert(index, operation);
        }
        else
        {
            recorder.DoAndPush(new AddCommand<Element>(Children, operation, index));
        }
    }

    public void UpdateTime(TimeSpan start, TimeSpan length, CommandRecorder? recorder = null)
    {
        if (start < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(start));

        if (length <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(length));

        if (recorder == null)
        {
            Start = start;
            Length = length;
        }
        else if (start != Start || length != Length)
        {
            recorder.DoAndPush(new UpdateTimeCommand(this, start, Start, length, Length));
        }
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

            foreach (LayerOperation item in Operations)
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

    internal bool InRange(TimeSpan ts)
    {
        return Start <= ts && ts < Length + Start;
    }

    private void Children_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Parent is not Scene scene) return;

        bool current = InRange(scene.CurrentFrame);

        if (current)
        {
            if (e.OldItems != null)
            {
                foreach (LayerOperation item in e.OldItems.OfType<LayerOperation>())
                {
                    item.EndingRender(Scope);
                }
            }

            foreach (LayerOperation item in Operations)
            {
                item.EndingRender(Scope);
            }

            Scope.Clear();

            foreach (LayerOperation item in Operations)
            {
                item.BeginningRender(Scope);
            }
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
