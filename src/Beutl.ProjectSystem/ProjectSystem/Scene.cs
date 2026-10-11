using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Beutl.Collections;
using Beutl.Language;
using Beutl.Media;

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

public partial class Scene : ProjectItem, INotifyEdited
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
    private readonly SceneRecovery _recovery;
    private TimeSpan _start = TimeSpan.FromMinutes(0);
    private TimeSpan _duration = TimeSpan.FromMinutes(5);
    private PixelSize _frameSize;

    public Scene()
        : this(1920, 1080, string.Empty)
    {
    }

    public Scene(int width, int height, string name)
    {
        _recovery = new SceneRecovery(this);
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

    internal SceneRecovery Recovery => _recovery;

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

        // The element keeps its ZIndex. The history does not see a write made after the element leaves
        // Children, so undoing the removal would bring the element back on the written layer.
        Children.Remove(element);
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

    private void Children_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ImmutableArray<TimeRange>.Builder affectedRange
            = ImmutableArray.CreateBuilder<TimeRange>(Math.Max(e.OldItems?.Count ?? 0, e.NewItems?.Count ?? 0));

        // Path.GetRelativePath の基点はディレクトリでなければならない。Uri.LocalPath は
        // .scene ファイル自身を指すため、そのまま使うと _excludeElements に "../foo.belm"
        // のような不正パスが入り、Deserialize 側 (Path.GetDirectoryName を使用) と整合せず
        // 除外パターンが効かない。結果として削除した Element が再読み込みで復活する。
        string? dirPath = Uri is { IsFile: true } sceneUri
            ? Path.GetDirectoryName(sceneUri.LocalPath)
            : null;
        if (e.Action == NotifyCollectionChangedAction.Remove
            && e.OldItems != null)
        {
            foreach (Element item in e.OldItems.OfType<Element>())
            {
                if (TryGetStoredElementPattern(dirPath, item, out string? itemPath, out string? rel)
                    && !_excludeElements.Contains(rel) && File.Exists(itemPath))
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
                if (TryGetStoredElementPattern(dirPath, item, out string? itemPath, out string? rel)
                    && _excludeElements.Contains(rel) && File.Exists(itemPath))
                {
                    _excludeElements.Remove(rel);
                }

                affectedRange.Add(item.Range);
            }
        }

        Edited?.Invoke(this, new ElementEditedEventArgs { AffectedRange = affectedRange.DrainToImmutable() });
    }

    // The element's file as a pattern relative to the scene directory, when both are on disk.
    private static bool TryGetStoredElementPattern(
        string? dirPath,
        Element item,
        [NotNullWhen(true)] out string? itemPath,
        [NotNullWhen(true)] out string? pattern)
    {
        if (dirPath is not null && item.Uri is { IsFile: true } itemUri)
        {
            itemPath = itemUri.LocalPath;
            pattern = NormalizeElementPattern(
                Path.GetRelativePath(dirPath, itemPath));
            return true;
        }

        itemPath = null;
        pattern = null;
        return false;
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
}
