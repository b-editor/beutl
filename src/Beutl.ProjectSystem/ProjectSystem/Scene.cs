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
    ThrowException = 0,

    Length = 1,

    Start = 1 << 1,

    // 空いている、ZIndexに配置します
    ZIndex = 1 << 2,

    Auto = Length | Start | ZIndex,

    Allow = 1 << 3
}

public partial class Scene : ProjectItem, INotifyEdited
{
    private readonly List<string> _includeElements = ["**/*.belm"];
    private readonly List<string> _excludeElements = [];
    private readonly Elements _children;
    private readonly HierarchicalList<TimelineLayer> _layers;
    private readonly HierarchicalList<SceneMarker> _markers;
    private readonly SceneRecovery _recovery;

    public Scene()
        : this(1920, 1080, string.Empty)
    {
    }

    public Scene(int width, int height, string name)
    {
        _recovery = new SceneRecovery(this);
        _children = new Elements(this);
        _layers = new HierarchicalList<TimelineLayer>(this);
        _markers = new HierarchicalList<SceneMarker>(this);
        // The lists are values rather than defaults, as their CoreProperty forms had no default.
        ChildrenProperty.Value = _children;
        GroupsProperty.Value = Groups;
        LayersProperty.Value = _layers;
        MarkersProperty.Value = _markers;
        DurationProperty.Value = TimeSpan.FromMinutes(5);
        ScanProperties<Scene>();
        FrameSizeProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(FrameSize)));
        StartProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Start)));
        DurationProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Duration)));
        FrameSize = new PixelSize(width, height);
        _children.CollectionChanged += Children_CollectionChanged;
        _children.Attached += item => item.Edited += OnElementEdited;
        _children.Detached += item => item.Edited -= OnElementEdited;
        _layers.CollectionChanged += Layers_CollectionChanged;
        _layers.Attached += OnLayerAttached;
        _layers.Detached += OnLayerDetached;
        // A plain list, so it names its owner for the write check itself.
        Groups.WriteOwner = this;
        Name = name;
    }

    internal SceneRecovery Recovery => _recovery;

    public event EventHandler? Edited;

    public IProperty<PixelSize> FrameSizeProperty { get; } = new ValueOnlyProperty<PixelSize>(default);

    // Serialize and Deserialize write and read the elements, groups and markers themselves.
    [NotAutoSerialized]
    public IProperty<Elements> ChildrenProperty { get; } = new ValueOnlyProperty<Elements>(null!) { KeepsList = true };

    [Display(Name = nameof(Strings.StartTime), ResourceType = typeof(Strings))]
    public IProperty<TimeSpan> StartProperty { get; } = new ValueOnlyProperty<TimeSpan>(TimeSpan.Zero) { Coerce = static (_, value) => NotNegative(value) };

    [Display(Name = nameof(Strings.DurationTime), ResourceType = typeof(Strings))]
    public IProperty<TimeSpan> DurationProperty { get; } = new ValueOnlyProperty<TimeSpan>(TimeSpan.Zero) { Coerce = static (_, value) => NotNegative(value) };

    [NotAutoSerialized]
    public IProperty<CoreList<ImmutableHashSet<Guid>>> GroupsProperty { get; } = new ValueOnlyProperty<CoreList<ImmutableHashSet<Guid>>>(null!) { KeepsList = true };

    public IProperty<CoreList<TimelineLayer>> LayersProperty { get; } = new ValueOnlyProperty<CoreList<TimelineLayer>>(null!) { KeepsList = true };

    [NotAutoSerialized]
    public IProperty<CoreList<SceneMarker>> MarkersProperty { get; } = new ValueOnlyProperty<CoreList<SceneMarker>>(null!) { KeepsList = true };

    public PixelSize FrameSize
    {
        get => FrameSizeProperty.Value;
        set => FrameSizeProperty.Value = value;
    }

    public TimeSpan Start
    {
        get => StartProperty.Value;
        set => StartProperty.Value = value;
    }

    public TimeSpan Duration
    {
        get => DurationProperty.Value;
        set => DurationProperty.Value = value;
    }

    public Elements Children
    {
        get => _children;
        set => _children.Replace(value);
    }

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

    public CoreList<SceneMarker> Markers
    {
        get => _markers;
        set => _markers.Replace(value);
    }

    private static TimeSpan NotNegative(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;

    public bool IsLayerLocked(int zIndex)
    {
        foreach (TimelineLayer layer in _layers)
        {
            if (layer.ZIndex == zIndex && layer.IsLocked) return true;
        }

        return false;
    }

    // Editor-only lock: an element cannot be edited when it or its layer is locked, or while
    // recovery keeps its original file because part of it could not be loaded. Saving would not
    // write such edits.
    public bool IsElementLocked(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return IsElementLockedByUser(element) || element.IsRecoveryProtected;
    }

    // The clip's own lock or its layer's, without recovery protection. Removing an element checks
    // only these, so an element whose file recovery keeps can still be deleted.
    internal bool IsElementLockedByUser(Element element)
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

    // Expects element.Uri to be set.
    public void AddChild(Element element,
        ElementOverlapHandling overlapHandling = ElementOverlapHandling.Auto)
    {
        ArgumentNullException.ThrowIfNull(element);

        AddChildCore(element, overlapHandling);
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

        MoveChildCore(zIndex, start, length, element);
    }

    public void MoveChildren(int deltaIndex, TimeSpan deltaStart, Element[] elements)
    {
        if (elements.Length < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(elements));
        }

        MoveChildrenCore(elements, deltaIndex, deltaStart);
    }

    private void Children_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ImmutableArray<TimeRange>.Builder affectedRange
            = ImmutableArray.CreateBuilder<TimeRange>(Math.Max(e.OldItems?.Count ?? 0, e.NewItems?.Count ?? 0));

        // Exclude patterns are relative to the scene's directory like Deserialize's, or a removed element comes back.
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
                if (TryGetStoredElementPattern(dirPath, item, out _, out string? rel))
                {
                    // Undo/Redo can reattach an element before auto-save recreates its file.
                    _excludeElements.RemoveAll(pattern => AreSameElementPaths(dirPath!, pattern, rel));
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
