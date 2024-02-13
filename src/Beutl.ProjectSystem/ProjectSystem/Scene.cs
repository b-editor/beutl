using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

using Beutl.Configuration;
using Beutl.Language;
using Beutl.Media;
using Beutl.Rendering.Cache;
using Beutl.Serialization;
using Beutl.Utilities;

using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Beutl.ProjectSystem;

// 要素を配置するとき、重なる部分の処理を定義します。
// 複数のフラグがある場合、
// 最初に長さを調整しようとします。
// 長さが0以下になる場合、開始位置を調整します。
// それでも、長さが0以下になる場合、もともとの長さでZIndexを変更します。
[Flags]
public enum ElementOverlapHandling
{
    // 例外を発生させます
    ThrowException = 0,

    // 長さを調整します
    Length = 1,

    // 開始位置を調整します
    Start = 1 << 1,

    // 空いている、ZIndexに配置します
    ZIndex = 1 << 2,

    Auto = Length | Start | ZIndex,

    Allow = 1 << 3
}

public class Scene : ProjectItem, IAffectsRender
{
    public static readonly CoreProperty<PixelSize> FrameSizeProperty;
    public static readonly CoreProperty<Elements> ChildrenProperty;
    public static readonly CoreProperty<TimeSpan> DurationProperty;
    private readonly List<string> _includeElements = ["**/*.belm"];
    private readonly List<string> _excludeElements = [];
    private readonly Elements _children;
    private TimeSpan _duration = TimeSpan.FromMinutes(5);
    private PixelSize _frameSize;

    public Scene()
        : this(1920, 1080, string.Empty)
    {
    }

    public Scene(int width, int height, string name)
    {
        FrameSize = new PixelSize(width, height);
        _children = new Elements(this);
        _children.CollectionChanged += Children_CollectionChanged;
        _children.Attached += item => item.Invalidated += OnElementInvalidated;
        _children.Detached += item => item.Invalidated -= OnElementInvalidated;
        Name = name;
    }

    static Scene()
    {
        FrameSizeProperty = ConfigureProperty<PixelSize, Scene>(nameof(FrameSize))
            .Accessor(o => o.FrameSize, (o, v) => o.FrameSize = v)
            .Register();

        ChildrenProperty = ConfigureProperty<Elements, Scene>(nameof(Children))
            .Accessor(o => o.Children, (o, v) => o.Children = v)
            .Register();

        DurationProperty = ConfigureProperty<TimeSpan, Scene>(nameof(Duration))
            .Accessor(o => o.Duration, (o, v) => o.Duration = v)
            .Register();
    }

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    public PixelSize FrameSize
    {
        get => _frameSize;
        set => SetAndRaise(FrameSizeProperty, ref _frameSize, value);
    }

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

    [NotAutoSerialized]
    public Elements Children
    {
        get => _children;
        set => _children.Replace(value);
    }

    // element.FileNameが既に設定されている状態
    public IRecordableCommand AddChild(Element element, ElementOverlapHandling overlapHandling = ElementOverlapHandling.Auto)
    {
        ArgumentNullException.ThrowIfNull(element);

        return new AddCommand(this, element, overlapHandling);
    }

    public IRecordableCommand DeleteChild(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return new DeleteCommand(this, element);
    }

    public IRecordableCommand RemoveChild(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return new RemoveCommand(this, element);
    }

    public IRecordableCommand MoveChild(int zIndex, TimeSpan start, TimeSpan length, Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (start < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(start));

        if (length <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(length));

        return new MoveCommand(
            zIndex: zIndex,
            element: element,
            newStart: start,
            oldStart: element.Start,
            newLength: length,
            oldLength: element.Length,
            scene: this);
    }

    public IRecordableCommand MoveChildren(int deltaIndex, TimeSpan deltaStart, Element[] elements)
    {
        if (elements.Length < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(elements));
        }

        return new MultipleMoveCommand(this, elements, deltaIndex, deltaStart);
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
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

        context.SetValue("Width", FrameSize.Width);
        context.SetValue("Height", FrameSize.Height);

        var elementsNode = new JsonObject();

        UpdateInclude();

        Process(elementsNode, "Include", _includeElements);
        Process(elementsNode, "Exclude", _excludeElements);

        context.SetValue("Elements", elementsNode);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

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

        if (context.Contains("Width") && context.Contains("Height"))
        {
            FrameSize = new PixelSize(context.GetValue<int>("Width"), context.GetValue<int>("Height"));
        }

        if (context.GetValue<JsonObject>(nameof(Elements)) is JsonObject elementsJson)
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

    protected override void SaveCore(string filename)
    {
        string? directory = Path.GetDirectoryName(filename);

        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        this.JsonSave2(filename);
    }

    protected override void RestoreCore(string filename)
    {
        this.JsonRestore2(filename);
    }

    private void SyncronizeFiles(IEnumerable<string> pathToElement)
    {
        using Activity? activity = BeutlApplication.ActivitySource.StartActivity("Scene.SyncronizeFiles");

        string baseDir = Path.GetDirectoryName(FileName)!;
        pathToElement = pathToElement.Select(x => Path.GetFullPath(x, baseDir)).ToArray();

        // 削除するElements
        Element[] toBeRemoved = Children.ExceptBy(pathToElement, x => x.FileName).ToArray();
        // 追加するElements
        string[] toBeAdded = pathToElement.Except(Children.Select(x => x.FileName)).ToArray();

        foreach (Element item in toBeRemoved)
        {
            Children.Remove(item);
        }

        Children.AddRange(toBeAdded.AsParallel().Select(item =>
        {
            var element = new Element();
            element.Restore(item);
            return element;
        }));

        activity?.SetTag("addCount", toBeAdded.Length);
        activity?.SetTag("removeCount", toBeRemoved.Length);
        activity?.SetTag("childrenCount", Children.Count);
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
        ImmutableArray<TimeRange>.Builder affectedRange
            = ImmutableArray.CreateBuilder<TimeRange>(Math.Max(e.OldItems?.Count ?? 0, e.NewItems?.Count ?? 0));

        if (e.Action == NotifyCollectionChangedAction.Remove
            && e.OldItems != null)
        {
            string dirPath = Path.GetDirectoryName(FileName)!;
            foreach (Element item in e.OldItems.OfType<Element>())
            {
                string rel = Path.GetRelativePath(dirPath, item.FileName).Replace('\\', '/');

                if (!_excludeElements.Contains(rel) && File.Exists(item.FileName))
                {
                    _excludeElements.Add(rel);
                }

                affectedRange.Add(item.Range);
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Add
            && e.NewItems != null)
        {
            string dirPath = Path.GetDirectoryName(FileName)!;
            foreach (Element item in e.NewItems.OfType<Element>())
            {
                string rel = Path.GetRelativePath(dirPath, item.FileName).Replace('\\', '/');

                if (_excludeElements.Contains(rel) && File.Exists(item.FileName))
                {
                    _excludeElements.Remove(rel);
                }

                affectedRange.Add(item.Range);
            }
        }

        Invalidated?.Invoke(this, new TimelineInvalidatedEventArgs(Children)
        {
            AffectedRange = affectedRange.DrainToImmutable()
        });
    }

    private void OnElementInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        Invalidated?.Invoke(this, e);
    }

    private int NearestLayerNumber(Element element)
    {
        if (IsOverlapping(element.Range, element.ZIndex))
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

    private Element? GetBefore(Element element)
    {
        Element? tmp = null;
        foreach (Element? item in Children.GetMarshal().Value)
        {
            if (item != element && item.ZIndex == element.ZIndex && item.Start < element.Range.End)
            {
                if (tmp == null || tmp.Start <= item.Start)
                {
                    tmp = item;
                }
            }
        }
        return tmp;
    }

    private Element? GetAfter(Element element)
    {
        Element? tmp = null;
        foreach (Element? item in Children.GetMarshal().Value)
        {
            if (item != element && item.ZIndex == element.ZIndex && item.Range.End > element.Range.End)
            {
                if (tmp == null || tmp.Range.End >= item.Range.End)
                {
                    tmp = item;
                }
            }
        }

        return tmp;
    }

    internal (Element? Before, Element? After, Element? Cover) GetBeforeAndAfterAndCover(Element element)
    {
        Element? beforeTmp = null;
        Element? afterTmp = null;
        Element? coverTmp = null;
        TimeRange range = element.Range;

        foreach (Element? item in Children.GetMarshal().Value)
        {
            if (item != element && item.ZIndex == element.ZIndex)
            {
                if (item.Start < range.Start
                    && (beforeTmp == null || beforeTmp.Start <= item.Start))
                {
                    beforeTmp = item;
                }

                if (item.Range.End > range.End
                    && (afterTmp == null || afterTmp.Range.End >= item.Range.End))
                {
                    afterTmp = item;
                }

                if (range.Contains(item.Range) || range == item.Range)
                {
                    coverTmp = item;
                }
            }
        }

        return (beforeTmp, afterTmp, coverTmp);
    }

    private bool IsOverlapping(TimeRange timeRange, int zindex)
    {
        return Children.Any(i =>
        {
            if (i.ZIndex == zindex)
            {
                if (i.Range == timeRange
                    || i.Range.Intersects(timeRange)
                    || i.Range.Contains(timeRange)
                    || timeRange.Contains(i.Range))
                {
                    return true;
                }
            }

            return false;
        });
    }

    private (TimeRange Range, int ZIndex) GetCorrectPosition(Element element, ElementOverlapHandling handling)
    {
        bool overlapping = IsOverlapping(element.Range, element.ZIndex);

        if (!overlapping || handling.HasFlag(ElementOverlapHandling.Allow))
            return (element.Range, element.ZIndex);

        if (handling == ElementOverlapHandling.ThrowException)
            throw new InvalidOperationException("要素の位置が無効です");

        (Element? before, Element? after, Element? cover) = GetBeforeAndAfterAndCover(element);
        var candidateStart = new List<TimeSpan>(2);
        var candidateEnd = new List<TimeSpan>(2);
        if (cover != null)
        {
            candidateEnd.Add(cover.Start);
            candidateStart.Add(cover.Range.End);
        }
        if (after != null) candidateEnd.Add(after.Start);
        if (before != null) candidateStart.Add(before.Range.End);

        TimeSpan start = element.Start;
        TimeSpan end = element.Range.End;

        if (handling.HasFlag(ElementOverlapHandling.Start) && handling.HasFlag(ElementOverlapHandling.Length))
        {
            foreach (TimeSpan cEnd in candidateEnd)
            {
                TimeRange range = TimeRange.FromRange(start, cEnd);
                if (range.Duration > TimeSpan.Zero && !IsOverlapping(range, element.ZIndex))
                {
                    return (range, element.ZIndex);
                }

                foreach (TimeSpan cStart in candidateStart)
                {
                    range = TimeRange.FromRange(cStart, cEnd);
                    if (range.Duration > TimeSpan.Zero && !IsOverlapping(range, element.ZIndex))
                    {
                        return (range, element.ZIndex);
                    }
                }
            }
        }

        if (handling.HasFlag(ElementOverlapHandling.Length))
        {
            foreach (TimeSpan item in candidateEnd)
            {
                TimeRange range = TimeRange.FromRange(start, item);
                if (range.Duration > TimeSpan.Zero && !IsOverlapping(range, element.ZIndex))
                {
                    return (range, element.ZIndex);
                }
            }
        }

        if (handling.HasFlag(ElementOverlapHandling.Start))
        {
            foreach (TimeSpan item in candidateStart)
            {
                TimeRange range = TimeRange.FromRange(item, end);
                if (range.Duration > TimeSpan.Zero && !IsOverlapping(range, element.ZIndex))
                {
                    return (range, element.ZIndex);
                }
            }
        }

        return (element.Range, NearestLayerNumber(element));
    }

    private sealed class AddCommand(Scene scene, Element element, ElementOverlapHandling overlapHandling) : IRecordableCommand
    {
        private readonly TimeSpan _oldSceneDuration = scene.Duration;
        private readonly bool _adjustSceneDuration = GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration;
        private int _zIndex;
        private TimeRange _range;

        public ImmutableArray<IStorable?> GetStorables() => [scene, element];

        public void Do()
        {
            (_range, _zIndex) = scene.GetCorrectPosition(element, overlapHandling);
            element.Start = _range.Start;
            element.Length = _range.Duration;
            element.ZIndex = _zIndex;
            scene.Children.Add(element);

            if (_adjustSceneDuration && scene.Duration < _range.End)
            {
                scene.Duration = _range.End;
            }
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            scene.Children.Remove(element);
            element.ZIndex = -1;
            if (_adjustSceneDuration)
            {
                scene.Duration = _oldSceneDuration;
            }
        }
    }

    private sealed class RemoveCommand(Scene scene, Element element) : IRecordableCommand
    {
        private int _zIndex;

        public ImmutableArray<IStorable?> GetStorables() => [scene, element];

        public void Do()
        {
            _zIndex = element.ZIndex;
            scene.Children.Remove(element);
            element.ZIndex = -1;
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            element.ZIndex = _zIndex;
            scene.Children.Add(element);
        }
    }

    private sealed class DeleteCommand : IRecordableCommand, IAffectsTimelineCommand
    {
        private readonly Scene _scene;
        private readonly TimeRange _timeRange;
        private readonly byte[] _jsonBytes;
        private string _fileName;
        private Element? _element;

        public DeleteCommand(Scene scene, Element element)
        {
            _scene = scene;
            _element = element;
            _fileName = element.FileName;
            _timeRange = element.Range;

            var jsonObject = new JsonObject();
            var context = new JsonSerializationContext(typeof(Element), NullSerializationErrorNotifier.Instance, json: jsonObject);
            using (ThreadLocalSerializationContext.Enter(context))
            {
                element.Serialize(context);
            }

            _jsonBytes = JsonSerializer.SerializeToUtf8Bytes(jsonObject);
        }

        public ImmutableArray<IStorable?> GetStorables() => [_scene];

        public ImmutableArray<TimeRange> GetAffectedRange() => [_timeRange];

        public void Do()
        {
            if (_element != null)
            {
                string fileName = _element.FileName;
                if (File.Exists(fileName))
                {
                    File.Delete(fileName);
                }

                _scene.Children.Remove(_element);
                _element = null;
            }
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            if (File.Exists(_fileName))
            {
                _fileName = RandomFileNameGenerator.Generate(Path.GetDirectoryName(_scene.FileName)!, "belm");
            }

            _element = new Element();

            JsonObject? jsonObject = JsonSerializer.Deserialize<JsonObject>(_jsonBytes);
            var context = new JsonSerializationContext(typeof(Element), NullSerializationErrorNotifier.Instance, json: jsonObject);
            using (ThreadLocalSerializationContext.Enter(context))
            {
                _element.Deserialize(context);
            }

            _element.Save(_fileName);

            _scene.Children.Add(_element);
        }
    }

    private sealed class MoveCommand(
        int zIndex,
        Element element,
        TimeSpan newStart, TimeSpan oldStart,
        TimeSpan newLength, TimeSpan oldLength,
        Scene scene) : IRecordableCommand, IAffectsTimelineCommand
    {
        private readonly int _oldZIndex = element.ZIndex;
        private readonly TimeSpan _oldSceneDuration = scene.Duration;
        private readonly bool _adjustSceneDuration = GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration;

        public bool Nothing => newStart == oldStart && newLength == oldLength && zIndex == _oldZIndex;

        public ImmutableArray<IStorable?> GetStorables() => [scene, element];

        public ImmutableArray<TimeRange> GetAffectedRange()
            => [new TimeRange(newStart, newLength), new TimeRange(oldStart, oldLength)];

        public void Do()
        {
            TimeSpan newEnd = newStart + newLength;
            (Element? before, Element? after, Element? cover) = element.GetBeforeAndAfterAndCover(zIndex, newStart, newEnd);

            if (before != null && before.Range.End >= newStart)
            {
                if ((after != null && (after.Start - before.Range.End) >= newLength) || after == null)
                {
                    element.Start = before.Range.End;
                    element.Length = newLength;
                    element.ZIndex = zIndex;
                }
                else
                {
                    Undo();
                }
            }
            else if (after != null && after.Start < newEnd)
            {
                TimeSpan ns = after.Start - newLength;
                if (((before != null && (after.Start - before.Range.End) >= newLength) || before == null) && ns >= TimeSpan.Zero)
                {
                    element.Start = ns;
                    element.Length = newLength;
                    element.ZIndex = zIndex;
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
                element.Start = newStart;
                element.Length = newLength;
                element.ZIndex = zIndex;
            }

            TimeRange range = element.Range;
            if (_adjustSceneDuration && scene.Duration < range.End)
            {
                scene.Duration = range.End;
            }
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            element.ZIndex = _oldZIndex;
            element.Start = oldStart;
            element.Length = oldLength;
            if (_adjustSceneDuration)
            {
                scene.Duration = _oldSceneDuration;
            }
        }
    }

    private sealed class MultipleMoveCommand : IRecordableCommand
    {
        private readonly Scene _scene;
        private readonly Element[] _elements;
        private readonly int _deltaZIndex;
        private readonly TimeSpan _deltaTime;
        private readonly bool _conflict;
        private readonly bool _adjustSceneDuration;
        private readonly TimeSpan _oldSceneDuration;
        private readonly TimeSpan _newSceneDuration;
        private readonly ImmutableArray<TimeRange> _affectedRange;

        public MultipleMoveCommand(
            Scene scene,
            Element[] elements,
            int deltaZIndex,
            TimeSpan deltaTime)
        {
            _scene = scene;
            _elements = elements;
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
            _adjustSceneDuration = GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration;

            if (_adjustSceneDuration)
            {
                _oldSceneDuration = _newSceneDuration = scene.Duration;

                TimeSpan maxEndingTime = elements.Max(i => i.Range.End + _deltaTime);
                if (_oldSceneDuration < maxEndingTime)
                {
                    _newSceneDuration = maxEndingTime;
                }
            }

            if (!_conflict)
            {
                _affectedRange = elements
                    .SelectMany(v => new[] { v.Range, v.Range.AddStart(_deltaTime) })
                    .ToImmutableArray();
            }
        }

        private bool HasConflict(Scene scene, int deltaZIndex, TimeSpan deltaTime)
        {
            Element[] others = scene.Children.Except(_elements).ToArray();
            foreach (Element item in _elements)
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
            (Element? before, Element? after, Element? _) = element.GetBeforeAndAfterAndCover(newIndex, newStart, _elements);

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

        public ImmutableArray<IStorable?> GetStorables()
        {
            if (_conflict)
                return [];

            return [_scene, .. _elements];
        }

        public ImmutableArray<TimeRange> GetAffectedRange() => _affectedRange;

        public void Do()
        {
            if (!_conflict)
            {
                foreach (Element item in _elements)
                {
                    item.Start += _deltaTime;
                    item.ZIndex += _deltaZIndex;
                }

                if (_adjustSceneDuration)
                {
                    _scene.Duration = _newSceneDuration;
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
                foreach (Element item in _elements)
                {
                    item.Start -= _deltaTime;
                    item.ZIndex -= _deltaZIndex;
                }

                if (_adjustSceneDuration)
                {
                    _scene.Duration = _oldSceneDuration;
                }
            }
        }
    }
}
