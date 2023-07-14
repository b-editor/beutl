using System.Collections.Specialized;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Beutl.Framework;
using Beutl.Language;
using Beutl.Media;
using Beutl.Rendering;

using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Beutl.ProjectSystem;

public class Scene : ProjectItem
{
    public static readonly CoreProperty<int> WidthProperty;
    public static readonly CoreProperty<int> HeightProperty;
    public static readonly CoreProperty<Elements> ChildrenProperty;
    public static readonly CoreProperty<TimeSpan> DurationProperty;
    public static readonly CoreProperty<TimeSpan> CurrentFrameProperty;
    public static readonly CoreProperty<IRenderer> RendererProperty;
    private readonly List<string> _includeElements = new()
    {
        "**/*.belm"
    };
    private readonly List<string> _excludeElements = new();
    private readonly Elements _children;
    private TimeSpan _duration = TimeSpan.FromMinutes(5);
    private TimeSpan _currentFrame;
    private IRenderer _renderer;

    public Scene()
        : this(1920, 1080, string.Empty)
    {
    }

    public Scene(int width, int height, string name)
    {
        _children = new Elements(this);
        _children.CollectionChanged += Children_CollectionChanged;
        Initialize(width, height);
        Name = name;
    }

    static Scene()
    {
        WidthProperty = ConfigureProperty<int, Scene>(nameof(Width))
            .Accessor(o => o.Width)
            .Register();

        HeightProperty = ConfigureProperty<int, Scene>(nameof(Height))
            .Accessor(o => o.Height)
            .Register();

        ChildrenProperty = ConfigureProperty<Elements, Scene>(nameof(Children))
            .Accessor(o => o.Children, (o, v) => o.Children = v)
            .Register();

        DurationProperty = ConfigureProperty<TimeSpan, Scene>(nameof(Duration))
            .Accessor(o => o.Duration, (o, v) => o.Duration = v)
            .Register();

        CurrentFrameProperty = ConfigureProperty<TimeSpan, Scene>(nameof(CurrentFrame))
            .Accessor(o => o.CurrentFrame, (o, v) => o.CurrentFrame = v)
            .Register();

        RendererProperty = ConfigureProperty<IRenderer, Scene>(nameof(Renderer))
            .Accessor(o => o.Renderer, (o, v) => o.Renderer = v)
            .Register();

        CurrentFrameProperty.Changed.Subscribe(e =>
        {
            if (e.Sender is Scene scene)
            {
                scene._renderer.RaiseInvalidated(e.NewValue);
            }
        });
    }

    public int Width => Renderer.Graphics.Size.Width;

    public int Height => Renderer.Graphics.Size.Height;

    [Display(Name = nameof(Strings.DurationTime), ResourceType = typeof(Strings))]
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

    [NotAutoSerialized]
    public Elements Children
    {
        get => _children;
        set => _children.Replace(value);
    }

    [NotAutoSerialized]
    public IRenderer Renderer
    {
        get => _renderer;
        private set => SetAndRaise(RendererProperty, ref _renderer, value);
    }

    [MemberNotNull(nameof(_renderer))]
    public void Initialize(int width, int height)
    {
        PixelSize oldSize = _renderer?.Graphics?.Size ?? PixelSize.Empty;
        _renderer?.Dispose();
        Renderer = new SceneRenderer(this, width, height);
        _renderer = Renderer;

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

    // element.FileNameが既に設定されている状態
    public IRecordableCommand AddChild(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.ZIndex = NearestLayerNumber(element);

        return new AddCommand(this, element);
    }

    public IRecordableCommand RemoveChild(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return new RemoveCommand(this, element);
    }

#pragma warning disable CA1822
    public IRecordableCommand MoveChild(int zIndex, TimeSpan start, TimeSpan length, Element element)
#pragma warning restore CA1822
    {
        ArgumentNullException.ThrowIfNull(element);

        if (start < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(start));

        if (length <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(length));

        return new MoveCommand(zIndex, element, start, element.Start, length, element.Length);
    }

    public IRecordableCommand MoveChildren(int deltaIndex, TimeSpan deltaStart, Element[] elements)
    {
        if (elements.Length < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(elements));
        }

        return new MultipleMoveCommand(this, elements, deltaIndex, deltaStart);
    }

    public override void ReadFromJson(JsonObject json)
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

        if (json.TryGetPropertyValue(nameof(Width), out JsonNode? widthNode)
            && json.TryGetPropertyValue(nameof(Height), out JsonNode? heightNode)
            && widthNode != null
            && heightNode != null
            && widthNode.AsValue().TryGetValue(out int width)
            && heightNode.AsValue().TryGetValue(out int height))
        {
            Initialize(width, height);
        }

        if (json.TryGetPropertyValue(nameof(Elements), out JsonNode? elementsNode)
            && elementsNode is JsonObject elementsJson)
        {
            var matcher = new Matcher();
            var directory = new DirectoryInfoWrapper(new DirectoryInfo(Path.GetDirectoryName(FileName)!));

            // 含めるクリップ
            if (elementsJson.TryGetPropertyValue("Include", out JsonNode? includeNode))
            {
                Process(matcher.AddInclude, includeNode!, _includeElements);
            }

            // 除外するクリップ
            if (elementsJson.TryGetPropertyValue("Exclude", out JsonNode? excludeNode))
            {
                Process(matcher.AddExclude, excludeNode!, _excludeElements);
            }

            PatternMatchingResult result = matcher.Execute(directory);
            SyncronizeFiles(result.Files.Select(x => x.Path));
        }
        else
        {
            Children.Clear();
        }
    }

    public override void WriteToJson(JsonObject json)
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

        base.WriteToJson(json);
        if (_renderer != null)
        {
            json[nameof(Width)] = _renderer.Graphics.Size.Width;
            json[nameof(Height)] = _renderer.Graphics.Size.Height;
        }

        var elementsNode = new JsonObject();

        UpdateInclude();

        Process(elementsNode, "Include", _includeElements);
        Process(elementsNode, "Exclude", _excludeElements);

        json["Elements"] = elementsNode;
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

    private void SyncronizeFiles(IEnumerable<string> pathToElement)
    {
        string baseDir = Path.GetDirectoryName(FileName)!;
        pathToElement = pathToElement.Select(x => Path.GetFullPath(x, baseDir)).ToArray();

        // 削除するElements
        IEnumerable<Element> toBeRemoved = Children.ExceptBy(pathToElement, x => x.FileName);
        // 追加するElements
        IEnumerable<string> toBeAdded = pathToElement.Except(Children.Select(x => x.FileName));

        foreach (Element item in toBeRemoved)
        {
            Children.Remove(item);
        }

        foreach (string item in toBeAdded)
        {
            var element = new Element();
            element.Restore(item);

            Children.Add(element);
        }
    }

    private void UpdateInclude()
    {
        string dirPath = Path.GetDirectoryName(FileName)!;
        var directory = new DirectoryInfoWrapper(new DirectoryInfo(dirPath));

        var matcher = new Matcher();
        matcher.AddIncludePatterns(_includeElements);
        matcher.AddExcludePatterns(_excludeElements);

        string[] files = matcher.Execute(directory).Files.Select(x => x.Path).ToArray();
        foreach (Element item in Children.GetMarshal().Value)
        {
            string rel = Path.GetRelativePath(dirPath, item.FileName).Replace('\\', '/');

            // 含まれていない場合追加
            if (!files.Contains(rel))
            {
                _includeElements.Add(rel);
            }
        }
    }

    private void Children_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Remove &&
            e.OldItems != null)
        {
            string dirPath = Path.GetDirectoryName(FileName)!;
            foreach (Element item in e.OldItems.OfType<Element>())
            {
                string rel = Path.GetRelativePath(dirPath, item.FileName).Replace('\\', '/');

                if (!_excludeElements.Contains(rel) && File.Exists(item.FileName))
                {
                    _excludeElements.Add(rel);
                }
            }
        }
    }

    private int NearestLayerNumber(Element element)
    {
        if (Children.Any(i => !(i.ZIndex != element.ZIndex
            || i.Range.Intersects(element.Range)
            || i.Range.Contains(element.Range)
            || element.Range.Contains(i.Range))))
        {
            int layerMax = Children.Max(i => i.ZIndex);

            // 使うことができるレイヤー番号
            var numbers = new List<int>();

            for (int l = 0; l <= layerMax; l++)
            {
                if (!Children.Any(i => i.ZIndex == l && i.Range.Intersects(element.Range)))
                {
                    numbers.Add(l);
                }
            }

            if (numbers.Count < 1)
            {
                return layerMax + 1;
            }

            return numbers.Nearest(element.ZIndex);
        }

        return element.ZIndex;
    }

    private sealed class AddCommand : IRecordableCommand
    {
        private readonly Scene _scene;
        private readonly Element _element;
        private readonly int _zIndex;

        public AddCommand(Scene scene, Element element)
        {
            _scene = scene;
            _element = element;
            _zIndex = element.ZIndex;
        }

        public AddCommand(Scene scene, Element element, int zIndex)
        {
            _scene = scene;
            _element = element;
            _zIndex = zIndex;
        }

        public void Do()
        {
            _element.ZIndex = _zIndex;
            _scene.Children.Add(_element);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _scene.Children.Remove(_element);
            _element.ZIndex = -1;
        }
    }

    private sealed class RemoveCommand : IRecordableCommand
    {
        private readonly Scene _scene;
        private readonly Element _element;
        private int _zIndex;

        public RemoveCommand(Scene scene, Element element)
        {
            _scene = scene;
            _element = element;
        }

        public void Do()
        {
            _zIndex = _element.ZIndex;
            _scene.Children.Remove(_element);
            _element.ZIndex = -1;
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _element.ZIndex = _zIndex;
            _scene.Children.Add(_element);
        }
    }

    private sealed class MoveCommand : IRecordableCommand
    {
        private readonly Element _element;
        private readonly int _zIndex;
        private readonly int _oldZIndex;
        private readonly TimeSpan _newStart;
        private readonly TimeSpan _oldStart;
        private readonly TimeSpan _newLength;
        private readonly TimeSpan _oldLength;

        public MoveCommand(
            int zIndex,
            Element element,
            TimeSpan newStart, TimeSpan oldStart,
            TimeSpan newLength, TimeSpan oldLength)
        {
            _element = element;
            _zIndex = zIndex;
            _oldZIndex = element.ZIndex;
            _newStart = newStart;
            _oldStart = oldStart;
            _newLength = newLength;
            _oldLength = oldLength;
        }

        public void Do()
        {
            TimeSpan newEnd = _newStart + _newLength;
            (Element? before, Element? after, Element? cover) = _element.GetBeforeAndAfterAndCover(_zIndex, _newStart, newEnd);

            if (before != null && before.Range.End >= _newStart)
            {
                if ((after != null && (after.Start - before.Range.End) >= _newLength) || after == null)
                {
                    _element.Start = before.Range.End;
                    _element.Length = _newLength;
                    _element.ZIndex = _zIndex;
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
                    _element.Start = ns;
                    _element.Length = _newLength;
                    _element.ZIndex = _zIndex;
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
                _element.Start = _newStart;
                _element.Length = _newLength;
                _element.ZIndex = _zIndex;
            }
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _element.ZIndex = _oldZIndex;
            _element.Start = _oldStart;
            _element.Length = _oldLength;
        }
    }

    private sealed class MultipleMoveCommand : IRecordableCommand
    {
        private readonly Element[] _element;
        private readonly int _deltaZIndex;
        private readonly TimeSpan _deltaTime;
        private readonly bool _conflict;

        public MultipleMoveCommand(
            Scene scene,
            Element[] elements,
            int deltaZIndex,
            TimeSpan deltaTime)
        {
            _element = elements;
            _deltaZIndex = deltaZIndex;
            _deltaTime = deltaTime;

            foreach (Element item in elements)
            {
                _conflict = HasConflict(scene, _deltaZIndex, _deltaTime);
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

            _conflict = HasConflict(scene, _deltaZIndex, _deltaTime);
        }

        private bool HasConflict(Scene scene, int deltaZIndex, TimeSpan deltaTime)
        {
            Element[] others = scene.Children.Except(_element).ToArray();
            foreach (Element item in _element)
            {
                TimeRange newRange = item.Range.AddStart(deltaTime);
                int newLayer = item.ZIndex + deltaZIndex;
                if (newLayer < 0 || newRange.Start.Ticks < 0)
                    return true;

                foreach (Element other in others)
                {
                    if (other.ZIndex == newLayer && other.Range.Intersects(newRange))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private TimeSpan? DeltaStart(Element element)
        {
            TimeSpan newStart = element.Start + _deltaTime;

            TimeSpan newEnd = newStart + element.Length;
            int newIndex = element.ZIndex + _deltaZIndex;
            (Element? before, Element? after, Element? _) = element.GetBeforeAndAfterAndCover(newIndex, newStart, _element);

            if (before != null && before.Range.End >= newStart)
            {
                if ((after != null && (after.Start - before.Range.End) >= element.Length) || after == null)
                {
                    return before.Range.End - element.Start;
                }
            }
            else if (after != null && after.Start < newEnd)
            {
                TimeSpan ns = after.Start - element.Length;
                if (((before != null && (after.Start - before.Range.End) >= element.Length) || before == null) && ns >= TimeSpan.Zero)
                {
                    return ns - element.Start;
                }
            }
            else if (newStart.Ticks < 0)
            {
                return -element.Start;
            }

            return null;
        }

        public void Do()
        {
            if (!_conflict)
            {
                foreach (Element item in _element)
                {
                    item.Start += _deltaTime;
                    item.ZIndex += _deltaZIndex;
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
                foreach (Element item in _element)
                {
                    item.Start -= _deltaTime;
                    item.ZIndex -= _deltaZIndex;
                }
            }
        }
    }
}
