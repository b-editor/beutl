using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

using BEditorNext.Language;

namespace BEditorNext.ProjectItems;

public class Clip : Element, IStorable
{
    public static readonly PropertyDefine<TimeSpan> StartProperty;
    public static readonly PropertyDefine<TimeSpan> LengthProperty;
    public static readonly PropertyDefine<int> LayerProperty;
    private TimeSpan _start;
    private TimeSpan _length;
    private int _layer;
    private string? _fileName;

    static Clip()
    {
        StartProperty = RegisterProperty<TimeSpan, Clip>(
            nameof(Start),
            (owner, obj) => owner.Start = obj,
            owner => owner.Start)
            .NotifyPropertyChanging(true)
            .NotifyPropertyChanged(true)
            .JsonName("start");

        LengthProperty = RegisterProperty<TimeSpan, Clip>(
            nameof(Length),
            (owner, obj) => owner.Length = obj,
            owner => owner.Length)
            .NotifyPropertyChanging(true)
            .NotifyPropertyChanged(true)
            .JsonName("length");

        LayerProperty = RegisterProperty<int, Clip>(
            nameof(Layer),
            (owner, obj) => owner.Layer = obj,
            owner => owner.Layer)
            .NotifyPropertyChanging(true)
            .NotifyPropertyChanged(true)
            .JsonName("layer");
    }

    public Clip()
    {
    }

    public Clip(Scene scene)
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

    public IEnumerable<RenderTask> Tasks => Children.OfType<RenderTask>();

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

    public void AddChild(RenderTask task, CommandRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (recorder == null)
        {
            Children.Add(task);
        }
        else
        {
            var command = new AddCommand(this, task, Children.Count);
            recorder.Do(command);
        }
    }

    public void RemoveChild(RenderTask task, CommandRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (recorder == null)
        {
            Children.Remove(task);
        }
        else
        {
            var command = new RemoveCommand(this, task);
            recorder.Do(command);
        }
    }

    public void InsertChild(int index, RenderTask task, CommandRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (recorder == null)
        {
            Children.Insert(index, task);
        }
        else
        {
            var command = new AddCommand(this, task, index);
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
            if (jobject.TryGetPropertyValue("tasks", out JsonNode? tasksNode) &&
                tasksNode is JsonArray tasksArray)
            {
                foreach (JsonObject task in tasksArray.OfType<JsonObject>())
                {
                    if (task.TryGetPropertyValue("@type", out JsonNode? atTypeNode) &&
                        atTypeNode is JsonValue atTypeValue &&
                        atTypeValue.TryGetValue(out string? atType))
                    {
                        var type = TypeResolver.ToType(atType);
                        RenderTask? renderTask = null;

                        if (type?.IsAssignableTo(typeof(RenderTask)) ?? false)
                        {
                            renderTask = Activator.CreateInstance(type) as RenderTask;
                        }

                        renderTask ??= new EmptyTask();
                        renderTask.FromJson(task);
                        Children.Add(renderTask);
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

            foreach (RenderTask item in Tasks)
            {
                JsonNode json = item.ToJson();
                if (item is not EmptyTask)
                {
                    json["@type"] = TypeResolver.ToString(item.GetType());
                }

                array.Add(json);
            }

            jobject["tasks"] = array;
        }

        return node;
    }

    private sealed class AddCommand : IRecordableCommand
    {
        private readonly Clip _clip;
        private readonly RenderTask _task;
        private readonly int _index;

        public AddCommand(Clip clip, RenderTask task, int index)
        {
            _clip = clip;
            _task = task;
            _index = index;
        }

        public string Name => CommandNameProvider.Instance.AddRenderTask;

        public void Do()
        {
            _clip.Children.Insert(_index, _task);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _clip.Children.Remove(_task);
        }
    }

    private sealed class RemoveCommand : IRecordableCommand
    {
        private readonly Clip _clip;
        private readonly RenderTask _task;
        private int _oldIndex;

        public RemoveCommand(Clip clip, RenderTask task)
        {
            _clip = clip;
            _task = task;
        }

        public string Name => CommandNameProvider.Instance.RemoveRenderTask;

        public void Do()
        {
            _oldIndex = _clip.Children.IndexOf(_task);
            _clip.Children.Remove(_task);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _clip.Children.Insert(_oldIndex, _task);
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
