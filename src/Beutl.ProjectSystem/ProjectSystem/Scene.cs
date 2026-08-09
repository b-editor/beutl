using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Beutl.Animation;
using Beutl.Collections;
using Beutl.Configuration;
using Beutl.Engine;
using Beutl.Engine.Expressions;
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
    private const int MaxRecoveredIdCollisionAttempts = 1024;
    private const string RecoveredDescendantIdsKey = "RecoveredDescendantIds";
    private const string RecoveredDescendantIdentitiesKey = "RecoveredDescendantIdentities";
    private const string RecoveredElementIdsKey = "RecoveredElementIds";
    private static readonly Guid s_recoveredElementNamespace = new("dfad2f76-1d04-5593-ae3b-f371fb1f42ee");
    private static readonly Regex s_idPattern = new(
        "\"Id\"\\s*:\\s*\"(?<id>[0-9a-fA-F-]{36})\"",
        RegexOptions.CultureInvariant);
    private static readonly Regex s_typePattern = new(
        "\"\\$type\"\\s*:\\s*(?<type>\"(?:\\\\.|[^\"\\\\])*\")",
        RegexOptions.CultureInvariant);
    private static readonly Regex s_legacyTypePattern = new(
        "\"@type\"\\s*:\\s*(?<type>\"(?:\\\\.|[^\"\\\\])*\")",
        RegexOptions.CultureInvariant);
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
    private readonly Dictionary<string, Guid> _recoveredDescendantIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _recoveredDescendantIdentities = new(StringComparer.Ordinal);
    private readonly Dictionary<CoreObject, (Guid OriginalId, Guid AssignedId, int Occurrence)> _recoveredDescendantRemaps
        = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, Guid> _recoveredElementIds = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, Guid> _pendingRecoveredElementIdMigrations = [];
    private readonly Dictionary<Guid, Guid> _pendingRecoveredDescendantIdMigrations = [];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<CoreObject, byte> _idlessRecoveredDescendants = new();
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
    /// When set, a gap that does not intersect this range is dropped, and the filter/ordering use the
    /// gap's intersection with the range, so a gap that merely straddles the range (its ends lie beyond
    /// a shortened or offset scene) is still reachable through its visible portion. The returned gap is
    /// the raw gap, unclamped — the caller decides how to clamp it for display.
    /// </param>
    public SceneGap? FindNextGap(TimeSpan currentTime, TimeRange? searchRange = null)
    {
        return EnumerateGaps()
            .Select(g => (Raw: g, Visible: ClampGap(g, searchRange)))
            .Where(x => x.Visible is { } v && v.Range.Start >= currentTime)
            .OrderBy(x => x.Visible!.Value.Range.Start)
            .ThenBy(x => x.Visible!.Value.Range.End)
            .Select(x => (SceneGap?)x.Raw)
            .FirstOrDefault();
    }

    /// <summary>
    /// Returns the last gap (across all ZIndexes) that ends at or before
    /// <paramref name="currentTime"/>, or <see langword="null"/> when no such
    /// gap exists. A playhead sitting inside a gap ends before that gap, so it is
    /// skipped, but one resting exactly on a gap's end still finds it.
    /// </summary>
    /// <param name="searchRange">
    /// When set, a gap that does not intersect this range is dropped, and the filter/ordering use the
    /// gap's intersection with the range, so a gap straddling the range stays reachable through its
    /// visible portion. The returned gap is the raw gap, unclamped.
    /// </param>
    public SceneGap? FindPreviousGap(TimeSpan currentTime, TimeRange? searchRange = null)
    {
        return EnumerateGaps()
            .Select(g => (Raw: g, Visible: ClampGap(g, searchRange)))
            .Where(x => x.Visible is { } v && v.Range.End <= currentTime)
            .OrderByDescending(x => x.Visible!.Value.Range.End)
            .ThenByDescending(x => x.Visible!.Value.Range.Start)
            .Select(x => (SceneGap?)x.Raw)
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
        RebuildRecoveredElementIds();
        if (_recoveredElementIds.Count > 0)
        {
            var recoveredElementIds = new JsonObject();
            foreach ((string path, Guid id) in _recoveredElementIds.OrderBy(static item => item.Key, StringComparer.Ordinal))
            {
                recoveredElementIds[path] = id.ToString();
            }

            context.SetValue(RecoveredElementIdsKey, recoveredElementIds);
        }

        if (_recoveredDescendantIds.Count > 0)
        {
            var recoveredDescendantIds = new JsonObject();
            foreach ((string key, Guid id) in _recoveredDescendantIds.OrderBy(
                         static item => item.Key,
                         StringComparer.Ordinal))
            {
                recoveredDescendantIds[key] = id.ToString();
            }

            context.SetValue(RecoveredDescendantIdsKey, recoveredDescendantIds);
        }

        if (_recoveredDescendantIdentities.Count > 0)
        {
            var recoveredDescendantIdentities = new JsonObject();
            foreach ((string key, Guid id) in _recoveredDescendantIdentities.OrderBy(
                         static item => item.Key,
                         StringComparer.Ordinal))
            {
                recoveredDescendantIdentities[key] = id.ToString();
            }

            context.SetValue(RecoveredDescendantIdentitiesKey, recoveredDescendantIdentities);
        }

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

        _pendingRecoveredElementIdMigrations.Clear();
        _pendingRecoveredDescendantIdMigrations.Clear();
        _idlessRecoveredDescendants.Clear();
        _recoveredDescendantIds.Clear();
        _recoveredDescendantIdentities.Clear();
        _recoveredDescendantRemaps.Clear();
        _recoveredElementIds.Clear();
        if (context.GetValue<JsonNode>(RecoveredElementIdsKey) is JsonObject recoveredElementIds)
        {
            foreach ((string path, JsonNode? idNode) in recoveredElementIds)
            {
                if (idNode is JsonValue idValue
                    && idValue.TryGetValue(out string? idText)
                    && Guid.TryParse(idText, out Guid id))
                {
                    _recoveredElementIds[NormalizeRelativePath(path)] = id;
                }
            }
        }

        if (context.GetValue<JsonNode>(RecoveredDescendantIdsKey) is JsonObject recoveredDescendantIds)
        {
            foreach ((string key, JsonNode? idNode) in recoveredDescendantIds)
            {
                if (idNode is JsonValue idValue
                    && idValue.TryGetValue(out string? idText)
                    && Guid.TryParse(idText, out Guid id))
                {
                    _recoveredDescendantIds[NormalizeRelativePath(key)] = id;
                }
            }
        }

        if (context.GetValue<JsonNode>(RecoveredDescendantIdentitiesKey) is JsonObject recoveredDescendantIdentities)
        {
            foreach ((string key, JsonNode? idNode) in recoveredDescendantIdentities)
            {
                if (idNode is JsonValue idValue
                    && idValue.TryGetValue(out string? idText)
                    && Guid.TryParse(idText, out Guid id))
                {
                    _recoveredDescendantIdentities[NormalizeRelativePath(key)] = id;
                }
            }
        }

        Markers.Clear();
        if (context.Contains(nameof(Markers))
            && context.GetValue<SceneMarker[]>(nameof(Markers)) is { } markers)
        {
            Markers.AddRange(markers);
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
                    .Select(id => _pendingRecoveredElementIdMigrations.GetValueOrDefault(id, id))
                    .Where(i => i != Guid.Empty && Children.Any(e => e.Id == i))
                    .ToImmutableHashSet();
                if (ids.Count >= 2)
                {
                    Groups.Add(ids);
                }
            }
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

        Children.AddRange(urisAdd.AsParallel().Select(RestoreElementOrFallback));
        ReassignDuplicateRecoveredIds();
        MigrateRecoveredElementReferences();

        activity?.SetTag("addCount", urisAdd.Length);
        activity?.SetTag("removeCount", elementsRemove.Length);
        activity?.SetTag("childrenCount", Children.Count);
    }

    private void ReassignDuplicateRecoveredIds()
    {
        string sceneDirectory = Path.GetDirectoryName(Uri!.LocalPath)!;
        var recoveredChildren = Children
            .Where(static child => child.SuppressedStorageSource is not null)
            .Select(child => (
                Child: child,
                RelativePath: NormalizeRelativePath(
                    Path.GetRelativePath(sceneDirectory, child.Uri!.LocalPath))))
            .OrderBy(static item => item.RelativePath, StringComparer.Ordinal)
            .ToArray();

        var claimedIds = new HashSet<Guid> { Guid.Empty, Id };
        foreach (CoreObject sceneObject in Layers.Cast<CoreObject>().Concat(Markers))
        {
            foreach (CoreObject graphObject in EnumerateSerializedGraphObjects(sceneObject).OfType<CoreObject>())
            {
                claimedIds.Add(graphObject.Id);
            }
        }

        var seenDescendants = new HashSet<CoreObject>(ReferenceEqualityComparer.Instance);
        var persistedDescendantIds = new Dictionary<string, Guid>(
            _recoveredDescendantIds,
            StringComparer.Ordinal);
        var persistedDescendantIdentities = new Dictionary<string, Guid>(
            _recoveredDescendantIdentities,
            StringComparer.Ordinal);
        var healthyChildren = Children
            .Where(static child => child.SuppressedStorageSource is null)
            .Select(child => (
                Child: child,
                RelativePath: NormalizeRelativePath(
                    Path.GetRelativePath(sceneDirectory, child.Uri!.LocalPath))))
            .OrderBy(static item => item.RelativePath, StringComparer.Ordinal)
            .ToArray();

        void ClaimHealthyDescendants(Element child)
        {
            foreach (CoreObject descendant in EnumerateSerializedGraphDescendants(child))
            {
                seenDescendants.Add(descendant);
                claimedIds.Add(descendant.Id);
            }
        }

        void ClaimPreviouslyRecoveredHealthyDescendants(Element child, string relativePath)
        {
            var occurrences = new Dictionary<Guid, int>();
            var legacyIndices = new Dictionary<CoreObject, int>(ReferenceEqualityComparer.Instance);
            int legacyIndex = 0;
            foreach (CoreObject descendant in EnumerateSerializedGraphDescendants(child))
            {
                legacyIndices.TryAdd(descendant, legacyIndex++);
            }

            foreach ((CoreObject descendant, SerializedGraphPath graphPath) in
                     EnumerateSerializedGraphDescendantPaths(child))
            {
                if (!seenDescendants.Add(descendant))
                {
                    continue;
                }

                string identityKey = CreateRecoveredDescendantIdentityKey(relativePath, graphPath.Stable);
                Guid originalId = descendant.Id;
                int occurrence = occurrences.GetValueOrDefault(originalId);
                occurrences[originalId] = occurrence + 1;
                string remapKey = CreateRecoveredDescendantKey(relativePath, originalId, occurrence);
                bool hasPersistedId = persistedDescendantIds.TryGetValue(remapKey, out Guid persistedId);
                bool hasPersistedIdentity = persistedDescendantIdentities.TryGetValue(
                    identityKey,
                    out Guid persistedIdentityId);
                bool ambiguousPositionalIdentity = false;
                if (!hasPersistedIdentity && graphPath.Positional != graphPath.Stable)
                {
                    string positionalIdentityKey = CreateRecoveredDescendantIdentityKey(
                        relativePath,
                        graphPath.Positional);
                    hasPersistedIdentity = persistedDescendantIdentities.TryGetValue(
                        positionalIdentityKey,
                        out persistedIdentityId);
                    if (hasPersistedIdentity
                        && !IsRecoveredDescendantPositionalIdentityUnambiguous(
                            persistedDescendantIdentities,
                            relativePath,
                            graphPath.Positional,
                            persistedIdentityId))
                    {
                        hasPersistedIdentity = false;
                        ambiguousPositionalIdentity = true;
                    }
                }

                if (!hasPersistedIdentity
                    && !ambiguousPositionalIdentity
                    && legacyIndices.TryGetValue(descendant, out int persistedIndex))
                {
                    string legacyIdentityKey = CreateLegacyRecoveredDescendantIdentityKey(
                        relativePath,
                        persistedIndex);
                    hasPersistedIdentity = persistedDescendantIdentities.TryGetValue(
                        legacyIdentityKey,
                        out persistedIdentityId);
                }

                bool hasPreviousAssignedId = hasPersistedId || hasPersistedIdentity;
                Guid previousAssignedId = hasPersistedId ? persistedId : persistedIdentityId;

                if (claimedIds.Add(originalId))
                {
                    if (hasPreviousAssignedId)
                    {
                        _pendingRecoveredDescendantIdMigrations.TryAdd(previousAssignedId, originalId);
                    }

                    continue;
                }

                Guid assignedId = hasPreviousAssignedId && claimedIds.Add(previousAssignedId)
                    ? previousAssignedId
                    : ClaimRecoveredDescendantId(relativePath, remapKey, claimedIds);
                descendant.Id = assignedId;
                _pendingRecoveredDescendantIdMigrations.TryAdd(
                    hasPreviousAssignedId ? previousAssignedId : assignedId,
                    assignedId);
            }
        }

        foreach ((Element child, string relativePath) in healthyChildren
                     .Where(item => !_recoveredElementIds.ContainsKey(item.RelativePath)))
        {
            claimedIds.Add(child.Id);
            ClaimHealthyDescendants(child);
        }

        foreach ((Element child, string relativePath) in healthyChildren
                     .Where(item => _recoveredElementIds.ContainsKey(item.RelativePath)))
        {
            if (!claimedIds.Add(child.Id))
            {
                Guid placeholderId = _recoveredElementIds[relativePath];
                child.Id = claimedIds.Add(placeholderId)
                    ? placeholderId
                    : ClaimRecoveredElementId(relativePath, claimedIds);
            }

            ClaimPreviouslyRecoveredHealthyDescendants(child, relativePath);
        }

        foreach ((Element child, string relativePath) in healthyChildren)
        {
            if (_recoveredElementIds.Remove(relativePath, out Guid placeholderId))
            {
                _pendingRecoveredElementIdMigrations.TryAdd(placeholderId, child.Id);
            }
        }

        var recoveredPaths = recoveredChildren
            .Select(static item => item.RelativePath)
            .ToHashSet(StringComparer.Ordinal);
        foreach (string path in _recoveredElementIds.Keys.Where(path => !recoveredPaths.Contains(path)).ToArray())
        {
            _recoveredElementIds.Remove(path);
        }

        var persistedChildren = new HashSet<Element>();
        foreach ((Element child, string relativePath) in recoveredChildren)
        {
            if (_recoveredElementIds.TryGetValue(relativePath, out Guid persistedId))
            {
                // A persisted remap that a healthy element now owns is stale; drop it so the
                // derivation loop assigns a fresh deterministic identity instead of a duplicate.
                if (claimedIds.Add(persistedId))
                {
                    child.Id = persistedId;
                    persistedChildren.Add(child);
                }
                else
                {
                    _recoveredElementIds.Remove(relativePath);
                }
            }
        }

        foreach ((Element child, string relativePath) in recoveredChildren)
        {
            if (persistedChildren.Contains(child))
            {
                continue;
            }

            if (claimedIds.Add(child.Id))
            {
                continue;
            }

            child.Id = ClaimRecoveredElementId(relativePath, claimedIds);
        }

        foreach ((Element child, string relativePath) in recoveredChildren)
        {
            _recoveredElementIds[relativePath] = child.Id;
        }

        _recoveredDescendantIds.Clear();
        _recoveredDescendantRemaps.Clear();
        var pendingDescendantRemaps
            = new Dictionary<CoreObject, (string RemapKey, Guid OriginalId, int Occurrence)>(
                ReferenceEqualityComparer.Instance);
        foreach ((Element child, string relativePath) in recoveredChildren)
        {
            var occurrences = new Dictionary<Guid, int>();
            int idlessOccurrence = 0;
            foreach (CoreObject descendant in EnumerateSerializedGraphDescendants(child))
            {
                if (!seenDescendants.Add(descendant))
                {
                    continue;
                }

                bool idless = _idlessRecoveredDescendants.ContainsKey(descendant);
                Guid originalId = idless ? Guid.Empty : descendant.Id;
                int occurrence = idless
                    ? idlessOccurrence++
                    : occurrences.GetValueOrDefault(originalId);
                if (!idless)
                {
                    occurrences[originalId] = occurrence + 1;
                }

                string remapKey = CreateRecoveredDescendantKey(relativePath, originalId, occurrence);
                if (persistedDescendantIds.TryGetValue(remapKey, out Guid persistedId))
                {
                    if (claimedIds.Add(persistedId))
                    {
                        descendant.Id = persistedId;
                        RecordRecoveredDescendantRemap(
                            descendant,
                            remapKey,
                            originalId,
                            persistedId,
                            occurrence);
                        continue;
                    }

                    pendingDescendantRemaps[descendant] = (remapKey, originalId, occurrence);
                }
                else if (!claimedIds.Add(originalId))
                {
                    pendingDescendantRemaps[descendant] = (remapKey, originalId, occurrence);
                }
            }
        }

        foreach ((Element child, string relativePath) in recoveredChildren)
        {
            foreach (CoreObject descendant in EnumerateSerializedGraphDescendants(child))
            {
                if (!pendingDescendantRemaps.Remove(
                        descendant,
                        out (string RemapKey, Guid OriginalId, int Occurrence) remap))
                {
                    continue;
                }

                Guid candidate = ClaimRecoveredDescendantId(relativePath, remap.RemapKey, claimedIds);
                descendant.Id = candidate;
                RecordRecoveredDescendantRemap(
                    descendant,
                    remap.RemapKey,
                    remap.OriginalId,
                    candidate,
                    remap.Occurrence);

                if (descendant is IFallback fallback)
                {
                    EnsureFallbackProjection(fallback);
                }
            }
        }

        // A global OriginalId -> AssignedId migration would redirect every reference that still
        // targets a surviving object. Only migrate when the original ID was abandoned entirely;
        // otherwise the references keep pointing at the object that retained it.
        var retainedIds = new HashSet<Guid> { Guid.Empty, Id };
        foreach (CoreObject graphObject in EnumerateSerializedGraphObjects(Children).OfType<CoreObject>())
        {
            retainedIds.Add(graphObject.Id);
        }

        foreach (CoreObject sceneObject in Layers.Cast<CoreObject>().Concat(Markers))
        {
            foreach (CoreObject graphObject in EnumerateSerializedGraphObjects(sceneObject).OfType<CoreObject>())
            {
                retainedIds.Add(graphObject.Id);
            }
        }

        foreach (Guid originalId in _pendingRecoveredElementIdMigrations.Keys.ToArray())
        {
            if (_pendingRecoveredElementIdMigrations[originalId] != originalId
                && retainedIds.Contains(originalId))
            {
                _pendingRecoveredElementIdMigrations.Remove(originalId);
            }
        }

        foreach (Guid originalId in _pendingRecoveredDescendantIdMigrations.Keys.ToArray())
        {
            if (_pendingRecoveredDescendantIdMigrations[originalId] != originalId
                && retainedIds.Contains(originalId))
            {
                _pendingRecoveredDescendantIdMigrations.Remove(originalId);
            }
        }
    }

    private static Guid ClaimRecoveredElementId(string relativePath, ISet<Guid> claimedIds)
    {
        for (int attempt = 0; attempt < MaxRecoveredIdCollisionAttempts; attempt++)
        {
            string candidateName = attempt == 0
                ? relativePath
                : $"{relativePath}#{attempt}";
            Guid candidate = CreateVersion5Guid(s_recoveredElementNamespace, candidateName);
            if (claimedIds.Add(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Could not assign a unique recovered element Id for '{relativePath}'.");
    }

    private static Guid ClaimRecoveredDescendantId(
        string relativePath,
        string remapKey,
        ISet<Guid> claimedIds)
    {
        for (int attempt = 0; attempt < MaxRecoveredIdCollisionAttempts; attempt++)
        {
            string candidateName = attempt == 0
                ? remapKey
                : $"{remapKey}#{attempt}";
            Guid candidate = CreateVersion5Guid(s_recoveredElementNamespace, candidateName);
            if (claimedIds.Add(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Could not assign a unique recovered descendant Id for '{relativePath}'.");
    }

    private void RecordRecoveredDescendantRemap(
        CoreObject descendant,
        string remapKey,
        Guid originalId,
        Guid assignedId,
        int occurrence)
    {
        _recoveredDescendantIds[remapKey] = assignedId;
        _recoveredDescendantRemaps[descendant] = (originalId, assignedId, occurrence);
        if (originalId != Guid.Empty)
        {
            _pendingRecoveredDescendantIdMigrations.TryAdd(originalId, assignedId);
        }

        if (descendant is IFallback fallback)
        {
            EnsureFallbackProjection(fallback);
        }
    }

    private Element RestoreElementOrFallback(Uri uri)
    {
        using DeserializationIncidents.Capture incidentCapture = DeserializationIncidents.BeginCapture();
        try
        {
            Element element = CoreSerializer.RestoreFromUri<Element>(uri);
            IFallback[] fallbacks = EnumerateSerializedGraphFallbacks(element).ToArray();
            int incidentCount = incidentCapture.Count;

            if (fallbacks.Length > 0 || incidentCount > 0)
            {
                var traversedFallbacks = new HashSet<IFallback>(
                    fallbacks,
                    ReferenceEqualityComparer.Instance);
                JsonObject[] untraversedFallbacks = incidentCapture.Fallbacks
                    .Where(fallback => !traversedFallbacks.Contains(fallback) && fallback.Json != null)
                    .Select(fallback => fallback.Json!.DeepClone().AsObject())
                    .ToArray();
                foreach (IFallback fallback in fallbacks)
                {
                    if (fallback is CoreObject fallbackObject)
                    {
                        if (TryGetSerializedId(fallback.Json, out Guid serializedId))
                        {
                            fallbackObject.Id = serializedId;
                        }
                        else
                        {
                            _idlessRecoveredDescendants.TryAdd(fallbackObject, 0);
                        }
                    }

                    EnsureFallbackProjection(fallback);
                }

                MarkRecoveredElement(
                    element,
                    File.ReadAllBytes(uri.LocalPath),
                    uri,
                    incidentCount > fallbacks.Length,
                    untraversedFallbacks);
            }

            return element;
        }
        // Any non-filesystem failure is a content problem the recovery path must absorb — value
        // converters throw freely (e.g. FormatException from Color.Parse); filesystem failures
        // still propagate so a genuinely unreadable project keeps failing loudly.
        catch (Exception ex) when (!ExceptionHelpers.ContainsFileSystemFailure(ex))
        {
            // Raw bytes, not text: the sidecar must survive rehoming byte-identically even when it
            // holds a BOM, another encoding, or undecodable bytes. The lossy decode is only scanned
            // for top-level recovery metadata.
            byte[] rawBytes = File.ReadAllBytes(uri.LocalPath);
            string rawText = DecodeRecoveryMetadata(rawBytes);
            byte[] metadataBytes = Encoding.UTF8.GetBytes(rawText);
            JsonObject? root = TryParseTopLevelObject(rawText);
            var element = new Element
            {
                Id = ResolveRecoveredElementId(metadataBytes, rawText, root, uri),
                Name = Path.GetFileNameWithoutExtension(uri.LocalPath),
                Uri = uri,
                IsEnabled = false,
            };
            string? topLevelTypeName = TryGetTopLevelTypeName(metadataBytes, rawText, root);
            FallbackReason fallbackReason = topLevelTypeName is not null
                                            && TypeFormat.ToType(topLevelTypeName) is null
                ? FallbackReason.TypeNotFound
                : FallbackReason.DeserializationFailed;
            var fallback = new FallbackEngineObject
            {
                Name = "Unreadable element data",
                Reason = fallbackReason,
                ErrorMessage = fallbackReason == FallbackReason.DeserializationFailed
                    ? $"{ex.GetType().Name}: {ex.Message}"
                    : null,
            };
            fallback.Json = CreateFallbackProjection(fallback, topLevelTypeName);
            element.AddObject(fallback);
            _idlessRecoveredDescendants.TryAdd(fallback, 0);
            MarkRecoveredElement(element, rawBytes, uri);
            return element;
        }
    }

    private static bool TryGetSerializedId(JsonObject? json, out Guid id)
    {
        id = Guid.Empty;
        return json is not null
               && json.TryGetPropertyValue(nameof(CoreObject.Id), out JsonNode? idNode)
               && idNode is JsonValue idValue
               && idValue.TryGetValue(out string? idText)
               && Guid.TryParse(idText, out id)
               && id != Guid.Empty;
    }

    private static void MarkRecoveredElement(
        Element element,
        byte[] rawBytes,
        Uri uri,
        bool hasNonFallbackIncidents = false,
        JsonObject[]? untraversedFallbacks = null)
    {
        element.SuppressedStorageSource = new SuppressedStorageSource(
            rawBytes,
            uri,
            hasNonFallbackIncidents,
            untraversedFallbacks);
    }

    internal static SuppressedStorageSource? TryResumeElementPersistence(Element element)
    {
        if (element.SuppressedStorageSource is not { } source
            || EnumerateSerializedGraphFallbacks(element).Any()
            || HasUnresolvedUntraversedFallback(element, source)
            || EnumerateSerializedGraphObjects(element).OfType<KeyFrame>().Any(static keyFrame => keyFrame.HasLossyEasing))
        {
            return null;
        }

        element.SuppressedStorageSource = null;
        return source;
    }

    private static bool HasUnresolvedUntraversedFallback(
        Element element,
        SuppressedStorageSource source)
    {
        if (source.UntraversedFallbacks is not { Length: > 0 } snapshots)
        {
            return false;
        }

        JsonObject current = CoreSerializer.SerializeToJsonObject(
            element,
            new CoreSerializerOptions { BaseUri = element.Uri });
        return snapshots.Any(snapshot => ContainsEquivalentJsonNode(current, snapshot));
    }

    private static bool ContainsEquivalentJsonNode(JsonNode? current, JsonNode snapshot)
    {
        if (JsonNode.DeepEquals(current, snapshot))
        {
            return true;
        }

        return current switch
        {
            JsonObject obj => obj.Any(item =>
                item.Value != null && ContainsEquivalentJsonNode(item.Value, snapshot)),
            JsonArray array => array.Any(item =>
                item != null && ContainsEquivalentJsonNode(item, snapshot)),
            _ => false,
        };
    }

    private static IEnumerable<IFallback> EnumerateSerializedGraphFallbacks(Element element)
    {
        return EnumerateSerializedGraphObjects(element).OfType<IFallback>();
    }

    private static IEnumerable<CoreObject> EnumerateSerializedGraphDescendants(Element element)
    {
        return EnumerateSerializedGraphObjects(element)
            .OfType<CoreObject>()
            .Where(value => !ReferenceEquals(value, element));
    }

    private void MigrateRecoveredElementReferences()
    {
        if (_pendingRecoveredElementIdMigrations.Count == 0
            && _pendingRecoveredDescendantIdMigrations.Count == 0)
        {
            return;
        }

        IEnumerable<CoreObject> ownerRoots = Children.Cast<CoreObject>()
            .Concat(Layers)
            .Concat(Markers);
        foreach (CoreObject ownerRoot in ownerRoots)
        {
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (EngineObject engineObject in EnumerateSerializedGraphObjects(ownerRoot).OfType<EngineObject>())
            {
                foreach (IProperty property in engineObject.Properties)
                {
                    object? currentValue = property.CurrentValue;
                    object? migratedValue = MigrateRecoveredReferenceValue(currentValue, visited);
                    if (!Equals(currentValue, migratedValue))
                    {
                        property.CurrentValue = migratedValue;
                    }

                    if (property.Expression is IReferenceExpression referenceExpression
                        && TryGetMigratedId(referenceExpression.ObjectId, out Guid migratedExpressionId)
                        && referenceExpression.Rebind(migratedExpressionId) is { } reboundExpression)
                    {
                        property.Expression = (IExpression)reboundExpression;
                    }

                    if (property.Animation is IKeyFrameAnimation animation)
                    {
                        foreach (IKeyFrame keyFrame in animation.KeyFrames)
                        {
                            object? keyFrameValue = keyFrame.Value;
                            object? migratedKeyFrameValue = MigrateRecoveredReferenceValue(keyFrameValue, visited);
                            if (!Equals(keyFrameValue, migratedKeyFrameValue))
                            {
                                keyFrame.Value = migratedKeyFrameValue;
                            }
                        }
                    }
                }
            }
        }
    }

    private bool TryGetMigratedId(Guid originalId, out Guid migratedId)
    {
        return _pendingRecoveredElementIdMigrations.TryGetValue(originalId, out migratedId)
               || _pendingRecoveredDescendantIdMigrations.TryGetValue(originalId, out migratedId);
    }

    private object ResolveMigratedReference(IReference reference, Guid migratedId)
    {
        CoreObject? target = EnumerateSerializedGraphObjects(Children)
            .OfType<CoreObject>()
            .FirstOrDefault(candidate => candidate.Id == migratedId);
        if (target is not null && reference.ObjectType.IsInstanceOfType(target))
        {
            return reference.Resolved(target);
        }

        try
        {
            return Activator.CreateInstance(reference.GetType(), migratedId) ?? reference;
        }
        catch (MissingMethodException)
        {
            return reference;
        }
    }

    private object? MigrateRecoveredReferenceValue(object? value, ISet<object> visited)
    {
        if (value is IReference reference)
        {
            return TryGetMigratedId(reference.Id, out Guid migratedId)
                ? ResolveMigratedReference(reference, migratedId)
                : value;
        }

        if (value is null or string
            || (!value.GetType().IsValueType && !visited.Add(value)))
        {
            return value;
        }

        if (value is IDictionary dictionary)
        {
            foreach (object key in dictionary.Keys.Cast<object>().ToArray())
            {
                object? item = dictionary[key];
                object? migratedItem = MigrateRecoveredReferenceValue(item, visited);
                if (!Equals(item, migratedItem))
                {
                    dictionary[key] = migratedItem;
                }
            }
        }
        else if (value is IList list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                object? item = list[i];
                object? migratedItem = MigrateRecoveredReferenceValue(item, visited);
                if (!Equals(item, migratedItem))
                {
                    list[i] = migratedItem;
                }
            }
        }

        return value;
    }

    private static IEnumerable<object> EnumerateSerializedGraphObjects(object root)
    {
        var objects = new List<object>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        CollectSerializedGraphObjects(root, visited, objects);
        return objects;
    }

    private static void CollectSerializedGraphObjects(
        object? value,
        ISet<object> visited,
        ICollection<object> objects)
    {
        if (value is null or string
            || (!value.GetType().IsValueType && !visited.Add(value)))
        {
            return;
        }

        if (value is CoreObject or IFallback)
        {
            objects.Add(value);
        }

        if (value is IHierarchical hierarchical)
        {
            foreach (IHierarchical child in hierarchical.HierarchicalChildren)
            {
                CollectSerializedGraphObjects(child, visited, objects);
            }
        }

        if (value is EngineObject engineObject)
        {
            foreach (IProperty property in engineObject.Properties)
            {
                CollectSerializedGraphObjects(property.CurrentValue, visited, objects);
                if (property.Animation is IKeyFrameAnimation animation)
                {
                    foreach (IKeyFrame keyFrame in animation.KeyFrames)
                    {
                        CollectSerializedGraphObjects(keyFrame, visited, objects);
                        CollectSerializedGraphObjects(keyFrame.Value, visited, objects);
                    }
                }
            }
        }

        if (value is System.Collections.IDictionary dictionary)
        {
            foreach (object? item in dictionary.Values)
            {
                CollectSerializedGraphObjects(item, visited, objects);
            }
        }
        else if (value is IEnumerable enumerable)
        {
            foreach (object? item in enumerable)
            {
                CollectSerializedGraphObjects(item, visited, objects);
            }
        }
    }

    private static IEnumerable<(CoreObject Object, SerializedGraphPath Path)> EnumerateSerializedGraphDescendantPaths(
        Element element)
    {
        var objects = new List<(CoreObject Object, SerializedGraphPath Path)>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        CollectSerializedGraphObjectPaths(element, new SerializedGraphPath("$", "$"), visited, objects);
        return objects.Where(item => !ReferenceEquals(item.Object, element));
    }

    private static void CollectSerializedGraphObjectPaths(
        object? value,
        SerializedGraphPath path,
        ISet<object> visited,
        ICollection<(CoreObject Object, SerializedGraphPath Path)> objects)
    {
        if (value is null or string
            || (!value.GetType().IsValueType && !visited.Add(value)))
        {
            return;
        }

        if (value is CoreObject coreObject)
        {
            objects.Add((coreObject, path));
        }

        if (value is Element element)
        {
            CollectSerializedGraphPathItems(
                element.Objects,
                AppendSerializedGraphPath(path, "property", nameof(Element.Objects)),
                visited,
                objects);
        }

        if (value is EngineObject engineObject)
        {
            foreach (IProperty property in engineObject.Properties)
            {
                SerializedGraphPath propertyPath = AppendSerializedGraphPath(path, "property", property.Name);
                CollectSerializedGraphObjectPaths(property.CurrentValue, propertyPath, visited, objects);
                if (property.Animation is IKeyFrameAnimation animation)
                {
                    SerializedGraphPath keyFramesPath = AppendSerializedGraphPath(
                        path,
                        "animation",
                        property.Name);
                    var occurrences = new Dictionary<Guid, int>();
                    int index = 0;
                    foreach (IKeyFrame keyFrame in animation.KeyFrames)
                    {
                        SerializedGraphPath keyFramePath = CreateSerializedGraphCollectionItemPath(
                            keyFramesPath,
                            keyFrame,
                            index++,
                            occurrences);
                        CollectSerializedGraphObjectPaths(keyFrame, keyFramePath, visited, objects);
                        CollectSerializedGraphObjectPaths(
                            keyFrame.Value,
                            AppendSerializedGraphPath(keyFramePath, "property", nameof(IKeyFrame.Value)),
                            visited,
                            objects);
                    }
                }
            }
        }

        if (value is IHierarchical hierarchical)
        {
            CollectSerializedGraphPathItems(
                hierarchical.HierarchicalChildren,
                AppendSerializedGraphPath(path, "collection", "HierarchicalChildren"),
                visited,
                objects);
        }

        if (value is System.Collections.IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                CollectSerializedGraphObjectPaths(
                    entry.Value,
                    AppendSerializedGraphPath(path, "key", entry.Key?.ToString() ?? "null"),
                    visited,
                    objects);
            }
        }
        else if (value is IEnumerable enumerable)
        {
            CollectSerializedGraphPathItems(enumerable, path, visited, objects);
        }
    }

    private static void CollectSerializedGraphPathItems(
        IEnumerable items,
        SerializedGraphPath path,
        ISet<object> visited,
        ICollection<(CoreObject Object, SerializedGraphPath Path)> objects)
    {
        var occurrences = new Dictionary<Guid, int>();
        int index = 0;
        foreach (object? item in items)
        {
            SerializedGraphPath itemPath = CreateSerializedGraphCollectionItemPath(
                path,
                item,
                index++,
                occurrences);
            CollectSerializedGraphObjectPaths(item, itemPath, visited, objects);
        }
    }

    private static SerializedGraphPath CreateSerializedGraphCollectionItemPath(
        SerializedGraphPath path,
        object? item,
        int index,
        IDictionary<Guid, int> occurrences)
    {
        string indexText = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string positional = AppendSerializedGraphPath(path.Positional, "index", indexText);
        if (item is CoreObject { Id: var id } && id != Guid.Empty)
        {
            int occurrence = occurrences.TryGetValue(id, out int value) ? value : 0;
            occurrences[id] = occurrence + 1;
            string stable = AppendSerializedGraphPath(path.Stable, "id", $"{id:D}#{occurrence}");
            return new SerializedGraphPath(stable, positional);
        }

        return new SerializedGraphPath(
            AppendSerializedGraphPath(path.Stable, "index", indexText),
            positional);
    }

    private static SerializedGraphPath AppendSerializedGraphPath(
        SerializedGraphPath path,
        string kind,
        string value)
    {
        return new SerializedGraphPath(
            AppendSerializedGraphPath(path.Stable, kind, value),
            AppendSerializedGraphPath(path.Positional, kind, value));
    }

    private static bool IsRecoveredDescendantPositionalIdentityUnambiguous(
        IReadOnlyDictionary<string, Guid> identities,
        string relativePath,
        string positionalPath,
        Guid expectedId)
    {
        string keyPrefix = $"{relativePath}!path:";
        string normalizedPath = NormalizeSerializedGraphPositionalPath(positionalPath);
        foreach ((string key, Guid id) in identities)
        {
            if (id != expectedId
                && key.StartsWith(keyPrefix, StringComparison.Ordinal)
                && NormalizeSerializedGraphPositionalPath(key[keyPrefix.Length..]) == normalizedPath)
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeSerializedGraphPositionalPath(string path)
    {
        string[] segments = path.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i].StartsWith("index:", StringComparison.Ordinal))
            {
                segments[i] = "index:*";
            }
        }

        return string.Join('/', segments);
    }

    private static string AppendSerializedGraphPath(string path, string kind, string value)
    {
        string escaped = value.Replace("~", "~0").Replace("/", "~1");
        return $"{path}/{kind}:{escaped}";
    }

    private readonly record struct SerializedGraphPath(string Stable, string Positional);

    private static void EnsureFallbackProjection(IFallback fallback)
    {
        if (fallback is not CoreObject coreObject)
        {
            return;
        }

        JsonObject json = fallback.Json ?? new JsonObject();
        if (!json.ContainsKey("$type") && !json.ContainsKey("@type"))
        {
            json.WriteDiscriminator(coreObject.GetType());
        }

        json[nameof(CoreObject.Id)] = coreObject.Id.ToString();
        fallback.Json = json;
    }

    private static JsonObject CreateFallbackProjection(FallbackEngineObject fallback, string? typeName = null)
    {
        var json = new JsonObject
        {
            [nameof(CoreObject.Id)] = fallback.Id.ToString(),
            [nameof(CoreObject.Name)] = fallback.Name,
        };
        if (typeName is not null)
        {
            json["$type"] = typeName;
        }
        else
        {
            json.WriteDiscriminator(typeof(FallbackEngineObject));
        }

        return json;
    }

    private static JsonObject? TryParseTopLevelObject(string rawText)
    {
        try
        {
            return JsonNode.Parse(rawText) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string DecodeRecoveryMetadata(byte[] rawBytes)
    {
        ReadOnlySpan<byte> bytes = rawBytes;
        if (bytes.Length >= 4
            && bytes[0] == 0xff && bytes[1] == 0xfe
            && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            return Encoding.UTF32.GetString(bytes[4..]);
        }

        if (bytes.Length >= 4
            && bytes[0] == 0x00 && bytes[1] == 0x00
            && bytes[2] == 0xfe && bytes[3] == 0xff)
        {
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true).GetString(bytes[4..]);
        }

        if (bytes.Length >= 3
            && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf)
        {
            return Encoding.UTF8.GetString(bytes[3..]);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe)
        {
            return Encoding.Unicode.GetString(bytes[2..]);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff)
        {
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static string? TryGetTopLevelTypeName(
        ReadOnlySpan<byte> rawBytes,
        string rawText,
        JsonObject? root)
    {
        if (root?.TryGetDiscriminator(out string? parsedTypeName) == true)
        {
            return parsedTypeName;
        }

        if (TryGetTopLevelStringProperty(rawBytes, "$type", out string? scannedTypeName))
        {
            return scannedTypeName;
        }

        if (TryGetTopLevelStringProperty(rawBytes, "@type", out string? scannedLegacyTypeName))
        {
            return scannedLegacyTypeName;
        }

        Match? match = FindTopLevelMatch(rawText, s_typePattern.Matches(rawText))
                       ?? FindTopLevelMatch(rawText, s_legacyTypePattern.Matches(rawText));
        if (match is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<string>(match.Groups["type"].Value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Guid ResolveRecoveredElementId(
        ReadOnlySpan<byte> rawBytes,
        string rawText,
        JsonObject? root,
        Uri uri)
    {
        if (TryGetSerializedId(root, out Guid parsedId))
        {
            return parsedId;
        }

        if (TryGetTopLevelStringProperty(rawBytes, nameof(CoreObject.Id), out string? scannedId)
            && Guid.TryParse(scannedId, out Guid scannedGuid)
            && scannedGuid != Guid.Empty)
        {
            return scannedGuid;
        }

        // Only a top-level Id may name the element: a nested object's or quoted Id would collide
        // with live objects, so anything else falls through to the deterministic filename Guid.
        MatchCollection matches = s_idPattern.Matches(rawText);
        Match? topLevelMatch = FindTopLevelMatch(rawText, matches);
        if (topLevelMatch != null
            && Guid.TryParse(topLevelMatch.Groups["id"].Value, out Guid topLevelId)
            && topLevelId != Guid.Empty)
        {
            return topLevelId;
        }

        string sceneDirectory = Path.GetDirectoryName(Uri!.LocalPath)!;
        string relativePath = NormalizeRelativePath(Path.GetRelativePath(sceneDirectory, uri.LocalPath));
        return CreateVersion5Guid(s_recoveredElementNamespace, relativePath);
    }

    private static bool TryGetTopLevelStringProperty(
        ReadOnlySpan<byte> rawBytes,
        string propertyName,
        out string? value)
    {
        value = null;
        if (rawBytes.Length >= 3
            && rawBytes[0] == 0xef
            && rawBytes[1] == 0xbb
            && rawBytes[2] == 0xbf)
        {
            rawBytes = rawBytes[3..];
        }

        var reader = new Utf8JsonReader(rawBytes, isFinalBlock: false, state: default);
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            int propertyDepth = reader.CurrentDepth + 1;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName
                    && reader.CurrentDepth == propertyDepth
                    && reader.ValueTextEquals(propertyName))
                {
                    if (reader.Read() && reader.TokenType == JsonTokenType.String)
                    {
                        value = reader.GetString();
                        return value is not null;
                    }

                    return false;
                }
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private void RebuildRecoveredElementIds()
    {
        if (Uri is null)
        {
            return;
        }

        var recoveredChildren = Children.Where(
                static child => child.SuppressedStorageSource is not null)
            .ToArray();
        if (recoveredChildren.Length == 0
            && _recoveredElementIds.Count == 0
            && _recoveredDescendantIds.Count == 0
            && _recoveredDescendantIdentities.Count == 0
            && _recoveredDescendantRemaps.Count == 0)
        {
            return;
        }

        string sceneDirectory = Path.GetDirectoryName(Uri.LocalPath)!;
        var descendantRemaps = new Dictionary<CoreObject, (Guid OriginalId, Guid AssignedId, int Occurrence)>(
            _recoveredDescendantRemaps,
            ReferenceEqualityComparer.Instance);
        _recoveredDescendantIds.Clear();
        _recoveredDescendantIdentities.Clear();
        _recoveredDescendantRemaps.Clear();
        _recoveredElementIds.Clear();
        foreach (Element child in recoveredChildren)
        {
            string relativePath = NormalizeRelativePath(
                Path.GetRelativePath(sceneDirectory, child.Uri!.LocalPath));
            _recoveredElementIds[relativePath] = child.Id;
            foreach ((CoreObject descendant, SerializedGraphPath graphPath) in
                     EnumerateSerializedGraphDescendantPaths(child))
            {
                if (descendant is IFallback)
                {
                    string identityKey = CreateRecoveredDescendantIdentityKey(relativePath, graphPath.Stable);
                    _recoveredDescendantIdentities[identityKey] = descendant.Id;
                    if (graphPath.Positional != graphPath.Stable)
                    {
                        string positionalIdentityKey = CreateRecoveredDescendantIdentityKey(
                            relativePath,
                            graphPath.Positional);
                        _recoveredDescendantIdentities[positionalIdentityKey] = descendant.Id;
                    }
                }

                if (descendantRemaps.TryGetValue(
                        descendant,
                        out (Guid OriginalId, Guid AssignedId, int Occurrence) remap)
                    && descendant.Id == remap.AssignedId)
                {
                    string remapKey = CreateRecoveredDescendantKey(
                        relativePath,
                        remap.OriginalId,
                        remap.Occurrence);
                    _recoveredDescendantIds[remapKey] = remap.AssignedId;
                    _recoveredDescendantRemaps[descendant] = remap;
                }
            }
        }
    }

    private static string CreateRecoveredDescendantKey(string relativePath, Guid originalId, int occurrence)
    {
        return $"{relativePath}!{originalId:D}#{occurrence}";
    }

    private static string CreateRecoveredDescendantIdentityKey(string relativePath, string graphPath)
    {
        return $"{relativePath}!path:{graphPath}";
    }

    private static string CreateLegacyRecoveredDescendantIdentityKey(string relativePath, int index)
    {
        return $"{relativePath}!@{index}";
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static Match? FindTopLevelMatch(string rawText, MatchCollection matches)
    {
        int rootStart = 0;
        while (rootStart < rawText.Length
               && (char.IsWhiteSpace(rawText[rootStart]) || rawText[rootStart] == '\uFEFF'))
        {
            rootStart++;
        }

        if (rootStart >= rawText.Length || rawText[rootStart] != '{')
        {
            return null;
        }

        int matchIndex = 0;
        int objectDepth = 0;
        int arrayDepth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = rootStart; i < rawText.Length && matchIndex < matches.Count; i++)
        {
            Match match = matches[matchIndex];
            if (i == match.Index)
            {
                if (!inString && objectDepth == 1 && arrayDepth == 0)
                {
                    return match;
                }

                matchIndex++;
            }

            char current = rawText[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (current == '\\')
                {
                    escaped = true;
                }
                else if (current == '"')
                {
                    inString = false;
                }
            }
            else if (current == '"')
            {
                inString = true;
            }
            else if (current == '{')
            {
                objectDepth++;
            }
            else if (current == '}' && objectDepth > 0)
            {
                objectDepth--;
                if (objectDepth == 0)
                {
                    return null;
                }
            }
            else if (current == '[')
            {
                arrayDepth++;
            }
            else if (current == ']' && arrayDepth > 0)
            {
                arrayDepth--;
            }
        }

        return null;
    }

    private static Guid CreateVersion5Guid(Guid namespaceId, string name)
    {
        byte[] namespaceBytes = namespaceId.ToByteArray();
        SwapGuidByteOrder(namespaceBytes);
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        byte[] source = new byte[namespaceBytes.Length + nameBytes.Length];
        namespaceBytes.CopyTo(source, 0);
        nameBytes.CopyTo(source, namespaceBytes.Length);

        byte[] hash = SHA1.HashData(source);
        hash[6] = (byte)((hash[6] & 0x0f) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        Array.Resize(ref hash, 16);
        SwapGuidByteOrder(hash);
        return new Guid(hash);
    }

    private static void SwapGuidByteOrder(Span<byte> bytes)
    {
        (bytes[0], bytes[3]) = (bytes[3], bytes[0]);
        (bytes[1], bytes[2]) = (bytes[2], bytes[1]);
        (bytes[4], bytes[5]) = (bytes[5], bytes[4]);
        (bytes[6], bytes[7]) = (bytes[7], bytes[6]);
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
                if (_element.SuppressedStorageSource is null && File.Exists(fileName))
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
