using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

using BEditorNext.Language;

namespace BEditorNext.ProjectSystem;

public class SceneLayer : Element, IStorable
{
    public static readonly PropertyDefine<TimeSpan> StartProperty;
    public static readonly PropertyDefine<TimeSpan> LengthProperty;
    public static readonly PropertyDefine<int> LayerProperty;
    private TimeSpan _start;
    private TimeSpan _length;
    private int _layer;
    private string? _fileName;

    static SceneLayer()
    {
        StartProperty = RegisterProperty<TimeSpan, SceneLayer>(
            nameof(Start),
            (owner, obj) => owner.Start = obj,
            owner => owner.Start)
            .NotifyPropertyChanging(true)
            .NotifyPropertyChanged(true)
            .JsonName("start");

        LengthProperty = RegisterProperty<TimeSpan, SceneLayer>(
            nameof(Length),
            (owner, obj) => owner.Length = obj,
            owner => owner.Length)
            .NotifyPropertyChanging(true)
            .NotifyPropertyChanged(true)
            .JsonName("length");

        LayerProperty = RegisterProperty<int, SceneLayer>(
            nameof(Layer),
            (owner, obj) => owner.Layer = obj,
            owner => owner.Layer)
            .NotifyPropertyChanging(true)
            .NotifyPropertyChanged(true)
            .JsonName("layer");
    }

    public SceneLayer()
    {
    }

    public SceneLayer(Scene scene)
        : this()
    {
        Parent = scene;
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

    public int Layer
    {
        get => _layer;
        set => SetAndRaise(LayerProperty, ref _layer, value);
    }

    public IEnumerable<RenderOperation> Operations => Children.OfType<RenderOperation>();

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

    public void AddChild(RenderOperation operation, CommandRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (recorder == null)
        {
            Children.Add(operation);
        }
        else
        {
            var command = new AddCommand(this, operation, Children.Count);
            recorder.Do(command);
        }
    }

    public void RemoveChild(RenderOperation operation, CommandRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (recorder == null)
        {
            Children.Remove(operation);
        }
        else
        {
            var command = new RemoveCommand(this, operation);
            recorder.Do(command);
        }
    }

    public void InsertChild(int index, RenderOperation operation, CommandRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (recorder == null)
        {
            Children.Insert(index, operation);
        }
        else
        {
            var command = new AddCommand(this, operation, index);
            recorder.Do(command);
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
            if (jobject.TryGetPropertyValue("operations", out JsonNode? operationsNode) &&
                operationsNode is JsonArray operationsArray)
            {
                foreach (JsonObject operationJson in operationsArray.OfType<JsonObject>())
                {
                    if (operationJson.TryGetPropertyValue("@type", out JsonNode? atTypeNode) &&
                        atTypeNode is JsonValue atTypeValue &&
                        atTypeValue.TryGetValue(out string? atType))
                    {
                        var type = TypeResolver.ToType(atType);
                        RenderOperation? operation = null;

                        if (type?.IsAssignableTo(typeof(RenderOperation)) ?? false)
                        {
                            operation = Activator.CreateInstance(type) as RenderOperation;
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

            foreach (RenderOperation item in Operations)
            {
                JsonNode json = item.ToJson();
                if (item is not EmptyOperation)
                {
                    json["@type"] = TypeResolver.ToString(item.GetType());
                }

                array.Add(json);
            }

            jobject["operations"] = array;
        }

        return node;
    }

    private sealed class AddCommand : IRecordableCommand
    {
        private readonly SceneLayer _layer;
        private readonly RenderOperation _operation;
        private readonly int _index;

        public AddCommand(SceneLayer layer, RenderOperation operation, int index)
        {
            _layer = layer;
            _operation = operation;
            _index = index;
        }

        public ResourceReference<string> Name => "AddRenderOperationString";

        public void Do()
        {
            _layer.Children.Insert(_index, _operation);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _layer.Children.Remove(_operation);
        }
    }

    private sealed class RemoveCommand : IRecordableCommand
    {
        private readonly SceneLayer _layer;
        private readonly RenderOperation _operation;
        private int _oldIndex;

        public RemoveCommand(SceneLayer layer, RenderOperation operation)
        {
            _layer = layer;
            _operation = operation;
        }

        public ResourceReference<string> Name => "ResourceRenderOperationString";

        public void Do()
        {
            _oldIndex = _layer.Children.IndexOf(_operation);
            _layer.Children.Remove(_operation);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _layer.Children.Insert(_oldIndex, _operation);
        }
    }

    internal static class TypeResolver
    {
        public static Type? ToType(string fullName)
        {
            string[] arr = fullName.Split(':');

            if (arr.Length == 1)
            {
                return Type.GetType(arr[0]);
            }
            else if (arr.Length == 2)
            {
                return Type.GetType($"{arr[0]}, {arr[1]}");
            }
            else
            {
                return null;
            }
        }

        public static string ToString(Type type)
        {
            string? asm = type.Assembly.GetName().Name;
            string tname = type.FullName!;

            if (asm == null)
            {
                return tname;
            }
            else
            {
                return $"{tname}:{asm}";
            }
        }
    }
}
