using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

using BEditorNext.Graphics;

using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace BEditorNext.ProjectItems;

public class Scene : Element, IStorable
{
    public static readonly PropertyDefine<int> WidthProperty;
    public static readonly PropertyDefine<int> HeightProperty;
    public static readonly PropertyDefine<TimeSpan> DurationProperty;
    public static readonly PropertyDefine<int> CurrentFrameProperty;
    public static readonly PropertyDefine<Clip?> SelectedItemProperty;
    public static readonly PropertyDefine<PreviewOptions?> PreviewOptionsProperty;
    public static readonly PropertyDefine<TimelineOptions> TimelineOptionsProperty;
    private string? _fileName;
    private TimeSpan _duration;
    private int _currentFrame;
    private Clip? _selectedItem;
    private PreviewOptions? _previewOptions;
    private TimelineOptions _timelineOptions;
    private readonly List<string> _includeClips = new()
    {
        "**/*.clip"
    };
    private readonly List<string> _excludeClips = new();

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
            .NotifyPropertyChanging(true)
            .JsonName("duration");

        CurrentFrameProperty = RegisterProperty<int, Scene>(nameof(CurrentFrame), (owner, obj) => owner.CurrentFrame = obj, owner => owner.CurrentFrame)
            .NotifyPropertyChanged(true)
            .NotifyPropertyChanging(true)
            .JsonName("currentFrame");

        SelectedItemProperty = RegisterProperty<Clip?, Scene>(nameof(SelectedItem), (owner, obj) => owner.SelectedItem = obj, owner => owner.SelectedItem)
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

    public IEnumerable<Clip> Clips => Children.OfType<Clip>();

    public Clip? SelectedItem
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

    // clip.FileNameが既に設定されている状態
    public void AddChild(Clip clip, CommandRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(clip);

        if (recorder == null)
        {
            InsertChild(clip);
        }
        else
        {
            recorder.Do(new AddCommand(this, clip));
        }
    }

    public void RemoveChild(Clip clip, CommandRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(clip);

        if (recorder == null)
        {
            clip.Layer = -1;
            Children.Remove(clip);
        }
        else
        {
            recorder.Do(new RemoveCommand(this, clip));
        }
    }

    public void InsertChild(int layer, Clip clip, CommandRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(clip);

        if (recorder == null)
        {
            clip.Layer = layer;
            InsertChild(clip);
        }
        else
        {
            recorder.Do(new AddCommand(this, clip, layer));
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

            if (jobject.TryGetPropertyValue("clips", out JsonNode? clipsNode) &&
                clipsNode is JsonObject clipsJson)
            {
                var matcher = new Matcher();
                var directory = new DirectoryInfoWrapper(new DirectoryInfo(Path.GetDirectoryName(FileName)!));

                // 含めるクリップ
                if (clipsJson.TryGetPropertyValue("include", out JsonNode? includeNode))
                {
                    Process(matcher.AddInclude, includeNode!, _includeClips);
                }

                // 除外するクリップ
                if (clipsJson.TryGetPropertyValue("exclude", out JsonNode? excludeNode))
                {
                    Process(matcher.AddExclude, excludeNode!, _excludeClips);
                }

                PatternMatchingResult result = matcher.Execute(directory);
                SyncronizeClips(result.Files.Select(x => x.Path));
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
            var clipsNode = new JsonObject();

            UpdateInclude();

            Process(clipsNode, "include", _includeClips);
            Process(clipsNode, "exclude", _excludeClips);

            jobject["clips"] = clipsNode;
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

    private void SyncronizeClips(IEnumerable<string> pathToClip)
    {
        string baseDir = Path.GetDirectoryName(FileName)!;
        pathToClip = pathToClip.Select(x => Path.GetFullPath(x, baseDir)).ToArray();

        // 削除するClips
        IEnumerable<Clip> toRemoveClips = Clips.ExceptBy(pathToClip, x => x.FileName);
        // 追加するClips
        IEnumerable<string> toAddClips = pathToClip.Except(Clips.Select(x => x.FileName));

        foreach (Clip item in toRemoveClips)
        {
            Children.Remove(item);
        }

        foreach (string item in toAddClips)
        {
            var clip = new Clip();
            clip.Restore(item);

            InsertChild(clip);
        }
    }

    private void UpdateInclude()
    {
        string dirPath = Path.GetDirectoryName(FileName)!;
        var directory = new DirectoryInfoWrapper(new DirectoryInfo(dirPath));

        var matcher = new Matcher();
        matcher.AddIncludePatterns(_includeClips);
        matcher.AddExcludePatterns(_excludeClips);

        string[] files = matcher.Execute(directory).Files.Select(x => x.Path).ToArray();
        foreach (Clip item in Clips)
        {
            string rel = Path.GetRelativePath(dirPath, item.FileName).Replace('\\', '/');

            // 含まれていない場合追加
            if (!files.Contains(rel))
            {
                _includeClips.Add(rel);
            }
        }
    }

    private void Children_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Remove &&
            e.OldItems != null)
        {
            foreach (Clip item in e.OldItems.OfType<Clip>())
            {
                _excludeClips.Add(item.FileName);
            }
        }
    }

    private void InsertChild(Clip clip)
    {
        Clip[] array = Children.OfType<Clip>().ToArray();

        for (int i = 0; i < array.Length - 1; i++)
        {
            Clip current = array[i];
            int nextIdx = i + 1;
            Clip next = array[nextIdx];

            int curLayer = current.Layer;
            int nextLayer = next.Layer;

            if (curLayer <= clip.Layer && clip.Layer <= nextLayer)
            {
                Children.Insert(nextIdx, clip);
                return;
            }
        }

        Children.Add(clip);
    }

    private sealed class AddCommand : IRecordableCommand
    {
        private readonly Scene _scene;
        private readonly Clip _clip;
        private readonly int _layer;

        public AddCommand(Scene scene, Clip clip)
        {
            _scene = scene;
            _clip = clip;
            _layer = clip.Layer;
        }

        public AddCommand(Scene scene, Clip clip, int layer)
        {
            _scene = scene;
            _clip = clip;
            _layer = layer;
        }

        public void Do()
        {
            _clip.Layer = _layer;
            _scene.InsertChild(_clip);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _clip.Layer = -1;
            _scene.Children.Remove(_clip);
        }
    }

    private sealed class RemoveCommand : IRecordableCommand
    {
        private readonly Scene _scene;
        private readonly Clip _clip;
        private int _layer;

        public RemoveCommand(Scene scene, Clip clip)
        {
            _scene = scene;
            _clip = clip;
        }

        public void Do()
        {
            _layer = _clip.Layer;
            _clip.Layer = -1;
            _scene.Children.Remove(_clip);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _clip.Layer = _layer;
            _scene.InsertChild(_clip);
        }
    }
}
