using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Collections;
using Beutl.Configuration;
using Beutl.Language;
using Beutl.Media;
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

public class Scene : ProjectItem, INotifyEdited
{
    public static readonly CoreProperty<PixelSize> FrameSizeProperty;
    public static readonly CoreProperty<Elements> ChildrenProperty;
    public static readonly CoreProperty<TimeSpan> StartProperty;
    public static readonly CoreProperty<TimeSpan> DurationProperty;
    public static readonly CoreProperty<CoreList<ImmutableHashSet<Guid>>> GroupsProperty;
    public static readonly CoreProperty<CoreList<TimelineLayer>> LayersProperty;
    public static readonly CoreProperty<CoreList<SceneMarker>> MarkersProperty;
    private readonly List<string> _includeElements = ["**/*.belm"];
    private readonly List<string> _excludeElements = [];
    private readonly Elements _children;
    private readonly HierarchicalList<TimelineLayer> _layers;
    private readonly HierarchicalList<SceneMarker> _markers;
    private TimeSpan _start = TimeSpan.FromMinutes(0);
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
        _children.Attached += item => item.Edited += OnElementEdited;
        _children.Detached += item => item.Edited -= OnElementEdited;
        _layers = new HierarchicalList<TimelineLayer>(this);
        _layers.CollectionChanged += Layers_CollectionChanged;
        _layers.Attached += OnLayerAttached;
        _layers.Detached += OnLayerDetached;
        _markers = new HierarchicalList<SceneMarker>(this);
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

        StartProperty = ConfigureProperty<TimeSpan, Scene>(nameof(Start))
            .Accessor(o => o.Start, (o, v) => o.Start = v)
            .Register();

        DurationProperty = ConfigureProperty<TimeSpan, Scene>(nameof(Duration))
            .Accessor(o => o.Duration, (o, v) => o.Duration = v)
            .Register();

        GroupsProperty = ConfigureProperty<CoreList<ImmutableHashSet<Guid>>, Scene>(nameof(Groups))
            .Accessor(o => o.Groups, (o, v) => o.Groups = v)
            .Register();

        LayersProperty = ConfigureProperty<CoreList<TimelineLayer>, Scene>(nameof(Layers))
            .Accessor(o => o.Layers, (o, v) => o.Layers = v)
            .Register();

        MarkersProperty = ConfigureProperty<CoreList<SceneMarker>, Scene>(nameof(Markers))
            .Accessor(o => o.Markers, (o, v) => o.Markers = v)
            .Register();
    }

    public event EventHandler? Edited;

    public PixelSize FrameSize
    {
        get => _frameSize;
        set => SetAndRaise(FrameSizeProperty, ref _frameSize, value);
    }

    [Display(Name = nameof(Strings.StartTime), ResourceType = typeof(Strings))]
    public TimeSpan Start
    {
        get => _start;
        set
        {
            if (value < TimeSpan.Zero)
                value = TimeSpan.Zero;

            SetAndRaise(StartProperty, ref _start, value);
        }
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

    [NotAutoSerialized]
    public CoreList<ImmutableHashSet<Guid>> Groups
    {
        get;
        set => field.Replace(value);
    } = [];

    public CoreList<TimelineLayer> Layers
    {
        get => _layers;
        set => _layers.Replace(value);
    }

    [NotAutoSerialized]
    public CoreList<SceneMarker> Markers
    {
        get => _markers;
        set => _markers.Replace(value);
    }

    public bool IsLayerLocked(int zIndex)
    {
        foreach (TimelineLayer layer in _layers)
        {
            if (layer.ZIndex == zIndex && layer.IsLocked) return true;
        }

        return false;
    }

    // Editor-only lock: an element cannot be edited when it or its layer is locked.
    public bool IsElementLocked(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.IsLocked || IsLayerLocked(element.ZIndex);
    }

    // Prunes ids from every group and disbands any group left with fewer than two
    // members. Returns true if any group changed.
    public bool RemoveElementsFromGroups(IReadOnlyCollection<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        bool removed = false;
        for (int i = Groups.Count - 1; i >= 0; i--)
        {
            ImmutableHashSet<Guid> group = Groups[i];
            if (!group.Overlaps(ids)) continue;

            ImmutableHashSet<Guid> updated = group.Except(ids);
            if (updated.Count >= 2)
            {
                Groups[i] = updated;
            }
            else
            {
                Groups.RemoveAt(i);
            }

            removed = true;
        }

        return removed;
    }

    // element.FileNameが既に設定されている状態
    public void AddChild(Element element,
        ElementOverlapHandling overlapHandling = ElementOverlapHandling.Auto)
    {
        ArgumentNullException.ThrowIfNull(element);

        new AddCommand(this, element, overlapHandling).Do();
    }

    public void DeleteChild(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        new DeleteCommand(this, element).Do();
    }

    public void RemoveChild(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        new RemoveCommand(this, element).Do();
    }

    public void MoveChild(int zIndex, TimeSpan start, TimeSpan length, Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (start < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(start));

        if (length <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(length));

        new MoveCommand(
            zIndex: zIndex,
            element: element,
            newStart: start,
            oldStart: element.Start,
            newLength: length,
            oldLength: element.Length,
            scene: this)
            .Do();
    }

    public void MoveChildren(int deltaIndex, TimeSpan deltaStart, Element[] elements)
    {
        if (elements.Length < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(elements));
        }

        new MultipleMoveCommand(this, elements, deltaIndex, deltaStart).Do();
    }

    /// <summary>
    /// Enumerates the gaps between adjacent elements on each ZIndex, ordered by
    /// ZIndex (ascending) then by gap start (ascending). A gap is the empty
    /// interval between one element's <see cref="Element.Range.End"/> and the
    /// next element's <see cref="Element.Start"/> on the same ZIndex when the
    /// next element starts strictly after the previous one ends. Overlapping
    /// or touching elements produce no gap, and the space before the first
    /// element on a ZIndex is not a gap.
    /// </summary>
    public IEnumerable<SceneGap> EnumerateGaps()
    {
        foreach (IGrouping<int, Element> zGroup in Children.GroupBy(e => e.ZIndex).OrderBy(g => g.Key))
        {
            List<Element> sorted = zGroup.OrderBy(e => e.Start).ThenBy(e => e.Range.End).ToList();
            if (sorted.Count == 0) continue;

            // The anchor is the run's furthest-ending element, so it ends exactly at the gap start.
            Element coveredEndElement = sorted[0];
            TimeSpan coveredEnd = coveredEndElement.Range.End;
            for (int i = 1; i < sorted.Count; i++)
            {
                Element next = sorted[i];
                if (next.Start > coveredEnd)
                {
                    yield return new SceneGap(zGroup.Key, new TimeRange(coveredEnd, next.Start - coveredEnd), coveredEndElement);
                }

                if (next.Range.End > coveredEnd)
                {
                    coveredEnd = next.Range.End;
                    coveredEndElement = next;
                }
            }
        }
    }

    /// <summary>
    /// Closes the first gap after the continuous-coverage run containing
    /// <paramref name="anchor"/> on <paramref name="anchor"/>'s ZIndex by
    /// shifting the subsequent unlocked elements on that ZIndex left by the gap
    /// size. An overlapping peer that extends the run past the anchor moves the
    /// target gap to the run's covered end. Returns <see langword="false"/> when
    /// there is no gap after the run (no next element, or the next element
    /// touches or overlaps the run), when the layer is locked, or when a locked
    /// element blocks every shiftable follower. Locked layers and locked
    /// elements are never moved; a locked element acts as an immovable barrier
    /// that halts the shift. Does not commit history; the caller owns the single
    /// <c>HistoryManager.Commit</c> boundary.
    /// </summary>
    public bool CloseGapAfter(Element anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        if (anchor.HierarchicalParent is not Scene scene || !ReferenceEquals(scene, this))
            return false;

        int z = anchor.ZIndex;
        // A locked layer's elements must not move, matching the other timeline mutation services.
        if (IsLayerLocked(z)) return false;

        List<Element> sorted = Children
            .Where(e => e.ZIndex == z)
            .OrderBy(e => e.Start)
            .ThenBy(e => e.Range.End)
            .ToList();
        if (sorted.Count == 0) return false;

        // The gap to close is the first empty interval after the continuous coverage run that
        // contains the anchor, so an earlier element covering past the anchor means no gap exists.
        TimeSpan coveredEnd = sorted[0].Range.End;
        bool anchorInRun = ReferenceEquals(sorted[0], anchor);
        for (int i = 1; i < sorted.Count; i++)
        {
            Element cur = sorted[i];
            if (cur.Start > coveredEnd)
            {
                if (anchorInRun)
                {
                    Element[] toShift = ShiftableAfter(z, cur.Start);
                    return toShift.Length != 0 && MoveChildrenAndDetectChange(toShift, coveredEnd - cur.Start);
                }

                coveredEnd = cur.Range.End;
                anchorInRun = ReferenceEquals(cur, anchor);
            }
            else
            {
                if (cur.Range.End > coveredEnd) coveredEnd = cur.Range.End;
                if (ReferenceEquals(cur, anchor)) anchorInRun = true;
            }
        }

        return false;
    }

    /// <summary>
    /// Closes every gap between elements on every ZIndex (the space before the
    /// first element on a ZIndex is not closed). Returns the number of gaps
    /// closed. Gaps are closed right-to-left within each ZIndex so earlier
    /// closes do not shift elements that later closes depend on. Locked layers
    /// are skipped and locked elements are never moved; a locked element acts as
    /// an immovable barrier that halts the shift. Does not commit history; the
    /// caller owns the single commit boundary.
    /// </summary>
    public int CloseAllGaps()
    {
        List<SceneGap> gaps = EnumerateGaps().ToList();
        if (gaps.Count == 0) return 0;

        int closed = 0;
        foreach (IGrouping<int, SceneGap> zGroup in gaps.GroupBy(g => g.ZIndex))
        {
            if (IsLayerLocked(zGroup.Key)) continue;

            foreach (SceneGap gap in zGroup.OrderByDescending(g => g.Range.Start))
            {
                TimeSpan delta = -gap.Range.Duration;
                if (delta == TimeSpan.Zero) continue;

                Element[] toShift = ShiftableAfter(zGroup.Key, gap.Range.End);
                if (toShift.Length == 0) continue;

                if (MoveChildrenAndDetectChange(toShift, delta))
                {
                    closed++;
                }
            }
        }

        return closed;
    }

    // The elements at or after fromStart that a gap close may slide left, in timeline order. A locked
    // element is a hard wall: iteration stops at the first locked start-group, so nothing at or beyond
    // it shifts across the lock (elements before it only move further left, never onto a lock).
    // Grouping by Start keeps same-start elements atomic, independent of Children enumeration order.
    private Element[] ShiftableAfter(int zIndex, TimeSpan fromStart)
    {
        if (IsLayerLocked(zIndex)) return [];

        var result = new List<Element>();
        foreach (IGrouping<TimeSpan, Element> startGroup in Children
            .Where(e => e.ZIndex == zIndex && e.Start >= fromStart)
            .GroupBy(e => e.Start)
            .OrderBy(g => g.Key))
        {
            if (startGroup.Any(e => e.IsLocked)) break;

            result.AddRange(startGroup);
        }

        return [.. result];
    }

    private bool MoveChildrenAndDetectChange(Element[] elements, TimeSpan deltaStart)
    {
        TimeSpan[] originalStarts = elements.Select(e => e.Start).ToArray();

        MoveChildren(0, deltaStart, elements);

        for (int i = 0; i < elements.Length; i++)
        {
            if (elements[i].Start != originalStarts[i])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the first gap (across all ZIndexes) that starts at or after
    /// <paramref name="currentTime"/>, or <see langword="null"/> when no such
    /// gap exists. A playhead sitting inside a gap starts past that gap, so it is
    /// skipped, but one resting exactly on a gap's start still finds it.
    /// </summary>
    /// <param name="searchRange">
    /// When set, each gap is clamped to its intersection with this range and gaps that do not
    /// intersect it are dropped, so navigation stays within the active scene range yet a gap that
    /// merely straddles the range (its ends lie beyond a shortened or offset scene) is still reachable
    /// through its visible portion. The returned range is the clamped, visible gap.
    /// </param>
    public SceneGap? FindNextGap(TimeSpan currentTime, TimeRange? searchRange = null)
    {
        return EnumerateGaps()
            .Select(g => ClampGap(g, searchRange))
            .Where(g => g is { } v && v.Range.Start >= currentTime)
            .OrderBy(g => g!.Value.Range.Start)
            .ThenBy(g => g!.Value.Range.End)
            .FirstOrDefault();
    }

    /// <summary>
    /// Returns the last gap (across all ZIndexes) that ends at or before
    /// <paramref name="currentTime"/>, or <see langword="null"/> when no such
    /// gap exists. A playhead sitting inside a gap ends before that gap, so it is
    /// skipped, but one resting exactly on a gap's end still finds it.
    /// </summary>
    /// <param name="searchRange">
    /// When set, each gap is clamped to its intersection with this range and gaps that do not
    /// intersect it are dropped, so a gap straddling the range stays reachable through its visible
    /// portion. The returned range is the clamped, visible gap.
    /// </param>
    public SceneGap? FindPreviousGap(TimeSpan currentTime, TimeRange? searchRange = null)
    {
        return EnumerateGaps()
            .Select(g => ClampGap(g, searchRange))
            .Where(g => g is { } v && v.Range.End <= currentTime)
            .OrderByDescending(g => g!.Value.Range.End)
            .ThenByDescending(g => g!.Value.Range.Start)
            .FirstOrDefault();
    }

    /// <summary>
    /// Returns the gap on <paramref name="zIndex"/> that contains <paramref name="time"/> (half-open,
    /// like <see cref="TimeRange.Contains(TimeSpan)"/>), or <see langword="null"/> when the point is
    /// not inside a gap on that layer. Used to close the gap under a right-click position.
    /// </summary>
    public SceneGap? FindGapAt(TimeSpan time, int zIndex)
    {
        return EnumerateGaps()
            .Where(g => g.ZIndex == zIndex && g.Range.Contains(time))
            .Select(g => (SceneGap?)g)
            .FirstOrDefault();
    }

    // The gap clamped to its intersection with range (ZIndex and Anchor preserved), or null when they
    // do not overlap with positive width — half-open, matching TimeRange, so a point-only touch at an
    // edge yields no gap. A straddling gap contributes its in-range slice instead of being dropped.
    private static SceneGap? ClampGap(SceneGap gap, TimeRange? range)
    {
        if (range is not { } r) return gap;

        TimeSpan start = gap.Range.Start > r.Start ? gap.Range.Start : r.Start;
        TimeSpan end = gap.Range.End < r.End ? gap.Range.End : r.End;
        return end > start ? gap with { Range = new TimeRange(start, end - start) } : null;
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
        context.SetValue("Groups", Groups.Select(ids => string.Join(':', ids)).ToArray());
        context.SetValue(nameof(Markers), Markers);

        if (context.Mode.HasFlag(CoreSerializationMode.SaveReferencedObjects))
        {
            foreach (Element item in Children)
            {
                CoreSerializer.StoreToUri(item, item.Uri!);
            }
        }

        if (context.Mode.HasFlag(CoreSerializationMode.EmbedReferencedObjects))
        {
            context.SetValue("Elements", Children);
        }
        else
        {
            var elementsNode = new JsonObject();

            UpdateInclude();

            Process(elementsNode, "Include", _includeElements);
            Process(elementsNode, "Exclude", _excludeElements);

            context.SetValue("Elements", elementsNode);
        }
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

        if (context.GetValue<JsonNode>(nameof(Elements)) is { } elementsJson)
        {
            if (elementsJson is JsonObject elementsObject)
            {
                var matcher = new Matcher();
                var directoryName = Path.GetDirectoryName(Uri!.LocalPath)!;
                var directory = new DirectoryInfoWrapper(new DirectoryInfo(directoryName));

                // 含めるクリップ
                if (elementsObject.TryGetPropertyValue("Include", out JsonNode? includeNode))
                {
                    Process(matcher.AddInclude, includeNode!, _includeElements);
                }

                // 除外するクリップ
                if (elementsObject.TryGetPropertyValue("Exclude", out JsonNode? excludeNode))
                {
                    Process(matcher.AddExclude, excludeNode!, _excludeElements);
                }

                PatternMatchingResult result = matcher.Execute(directory);
                SyncronizeFiles(result.Files.Select(x => x.Path));
            }
            else
            {
                Children.Replace(context.GetValue<Elements>(nameof(Elements))!);
            }
        }
        else
        {
            Children.Clear();
        }

        if (context.Contains("Groups"))
        {
            string[]? groups = context.GetValue<string[]>("Groups");
            Groups.Clear();
            foreach (string group in groups ?? [])
            {
                var ids = group.Split(':')
                    .Select(s => Guid.TryParse(s, out Guid id) ? id : Guid.Empty)
                    .Where(i => i != Guid.Empty && Children.Any(e => e.Id == i))
                    .ToImmutableHashSet();
                if (ids.Count >= 2)
                {
                    Groups.Add(ids);
                }
            }
        }

        Markers.Clear();
        if (context.Contains(nameof(Markers))
            && context.GetValue<SceneMarker[]>(nameof(Markers)) is { } markers)
        {
            Markers.AddRange(markers);
        }
    }

    private void SyncronizeFiles(IEnumerable<string> pathToElement)
    {
        using Activity? activity = BeutlApplication.ActivitySource.StartActivity("Scene.SyncronizeFiles");

        var uriToElement = pathToElement.Select(x => new Uri(Uri!, Uri.UnescapeDataString(x))).ToArray();

        // 削除するElements
        Element[] elementsRemove = Children.ExceptBy(uriToElement, x => x.Uri).ToArray();
        // 追加するElements
        Uri[] urisAdd = uriToElement.Except(Children.Select(x => x.Uri).Where(u => u != null)).ToArray()!;

        foreach (Element item in elementsRemove)
        {
            Children.Remove(item);
        }

        Children.AddRange(urisAdd.AsParallel().Select(CoreSerializer.RestoreFromUri<Element>));

        activity?.SetTag("addCount", urisAdd.Length);
        activity?.SetTag("removeCount", elementsRemove.Length);
        activity?.SetTag("childrenCount", Children.Count);
    }

    private void UpdateInclude()
    {
        string dirPath = Path.GetDirectoryName(Uri!.LocalPath)!;
        var directory = new DirectoryInfoWrapper(new DirectoryInfo(dirPath));

        var matcher = new Matcher();
        matcher.AddIncludePatterns(_includeElements);
        matcher.AddExcludePatterns(_excludeElements);

        string[] files = matcher.Execute(directory).Files.Select(x => x.Path).ToArray();
        foreach (Element item in Children)
        {
            string rel = Path.GetRelativePath(dirPath, item.Uri!.LocalPath);

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

        // Path.GetRelativePath の基点はディレクトリでなければならない。Uri.LocalPath は
        // .scene ファイル自身を指すため、そのまま使うと _excludeElements に "../foo.belm"
        // のような不正パスが入り、Deserialize 側 (Path.GetDirectoryName を使用) と整合せず
        // 除外パターンが効かない。結果として削除した Element が再読み込みで復活する。
        string dirPath = Path.GetDirectoryName(Uri!.LocalPath)!;
        if (e.Action == NotifyCollectionChangedAction.Remove
            && e.OldItems != null)
        {
            foreach (Element item in e.OldItems.OfType<Element>())
            {
                string itemPath = item.Uri!.LocalPath;
                string rel = Path.GetRelativePath(dirPath, itemPath);

                if (!_excludeElements.Contains(rel) && File.Exists(itemPath))
                {
                    _excludeElements.Add(rel);
                }

                affectedRange.Add(item.Range);
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Add
                 && e.NewItems != null)
        {
            foreach (Element item in e.NewItems.OfType<Element>())
            {
                string itemPath = item.Uri!.LocalPath;
                string rel = Path.GetRelativePath(dirPath, itemPath);

                if (_excludeElements.Contains(rel) && File.Exists(itemPath))
                {
                    _excludeElements.Remove(rel);
                }

                affectedRange.Add(item.Range);
            }
        }

        Edited?.Invoke(this, new ElementEditedEventArgs { AffectedRange = affectedRange.DrainToImmutable() });
    }

    private void Layers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Only a layer that carries a compositional flag changes the rendered
        // output when added/removed; a default or lock-only model (materialized
        // or pruned by a lock toggle) is editor-only, mirroring OnLayerPropertyChanged.
        if (AnyCompositionalLayer(e.NewItems) || AnyCompositionalLayer(e.OldItems))
        {
            Edited?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool AnyCompositionalLayer(System.Collections.IList? items)
    {
        if (items is null) return false;
        foreach (object? item in items)
        {
            if (item is TimelineLayer { IsVideoMuted: true } or TimelineLayer { IsAudioMuted: true }
                or TimelineLayer { IsSolo: true })
            {
                return true;
            }
        }

        return false;
    }

    private void OnLayerAttached(TimelineLayer layer)
    {
        layer.PropertyChanged += OnLayerPropertyChanged;
    }

    private void OnLayerDetached(TimelineLayer layer)
    {
        layer.PropertyChanged -= OnLayerPropertyChanged;
    }

    private void OnLayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Edited triggers a preview re-render; Name/Color/IsLocked are editor-only
        // and must not. ZIndex retargets existing mute/solo flags, so it counts.
        if (e.PropertyName is nameof(TimelineLayer.IsVideoMuted)
            or nameof(TimelineLayer.IsAudioMuted)
            or nameof(TimelineLayer.IsSolo)
            or nameof(TimelineLayer.ZIndex))
        {
            Edited?.Invoke(sender, EventArgs.Empty);
        }
    }

    private void OnElementEdited(object? sender, EventArgs e)
    {
        Edited?.Invoke(sender, e);
    }

    private int NearestLayerNumber(Element element)
    {
        if (IsOverlapping(element.Range, element.ZIndex))
        {
            int layerMax = Children.Max(i => i.ZIndex);

            // 使うことができるレイヤー番号。ロックされたレイヤーには自動配置しない。
            var numbers = new List<int>();

            for (int l = 0; l <= layerMax; l++)
            {
                if (!IsLayerLocked(l)
                    && !Children.Any(i => i.ZIndex == l && i.Range.Intersects(element.Range)))
                {
                    numbers.Add(l);
                }
            }

            if (numbers.Count < 1)
            {
                int next = layerMax + 1;
                while (IsLayerLocked(next)) next++;
                return next;
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

    private sealed class AddCommand(Scene scene, Element element, ElementOverlapHandling overlapHandling)
    {
        private readonly bool _adjustSceneDuration = GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration;
        private int _zIndex;
        private TimeRange _range;

        public void Do()
        {
            (_range, _zIndex) = scene.GetCorrectPosition(element, overlapHandling);
            element.Start = _range.Start;
            element.Length = _range.Duration;
            element.ZIndex = _zIndex;
            scene.Children.Add(element);

            if (_adjustSceneDuration && scene.Duration + scene.Start < _range.End)
            {
                scene.Duration = _range.End - scene.Start;
            }
        }
    }

    private sealed class RemoveCommand(Scene scene, Element element)
    {
        public void Do()
        {
            scene.Children.Remove(element);
            element.ZIndex = -1;
        }
    }

    private sealed class DeleteCommand
    {
        private readonly Scene _scene;
        private Element? _element;

        public DeleteCommand(Scene scene, Element element)
        {
            _scene = scene;
            _element = element;
        }

        public void Do()
        {
            if (_element != null)
            {
                string fileName = _element.Uri!.LocalPath;
                if (File.Exists(fileName))
                {
                    File.Delete(fileName);
                }

                _scene.Children.Remove(_element);
                _element = null;
            }
        }
    }

    private sealed class MoveCommand(
        int zIndex,
        Element element,
        TimeSpan newStart,
        TimeSpan oldStart,
        TimeSpan newLength,
        TimeSpan oldLength,
        Scene scene)
    {
        private readonly int _oldZIndex = element.ZIndex;
        private readonly TimeSpan _oldSceneDuration = scene.Duration;
        private readonly bool _adjustSceneDuration = GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration;

        public void Do()
        {
            TimeSpan newEnd = newStart + newLength;
            (Element? before, Element? after, Element? cover) =
                element.GetBeforeAndAfterAndCover(zIndex, newStart, newEnd);

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
                if (((before != null && (after.Start - before.Range.End) >= newLength) || before == null) &&
                    ns >= TimeSpan.Zero)
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
            if (_adjustSceneDuration && scene.Duration + scene.Start < range.End)
            {
                scene.Duration = range.End - scene.Start;
            }
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

    private sealed class MultipleMoveCommand
    {
        private readonly Scene _scene;
        private readonly Element[] _elements;
        private readonly int _deltaZIndex;
        private readonly TimeSpan _deltaTime;
        private readonly bool _conflict;
        private readonly bool _adjustSceneDuration;
        private readonly TimeSpan _oldSceneDuration;
        private readonly TimeSpan _newSceneDuration;

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
                if (_oldSceneDuration + scene.Start < maxEndingTime)
                {
                    _newSceneDuration = maxEndingTime - scene.Start;
                }
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
            (Element? before, Element? after, Element? _) =
                element.GetBeforeAndAfterAndCover(newIndex, newStart, _elements);

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
                if (((before != null && (after.Start - before.Range.End) >= element.Length) || before == null) &&
                    ns >= TimeSpan.Zero)
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
    }
}
