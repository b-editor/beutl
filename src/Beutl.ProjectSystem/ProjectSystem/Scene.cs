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
    private readonly SceneRecovery _recovery;

    public Scene()
        : this(1920, 1080, string.Empty)
    {
    }

    public Scene(int width, int height, string name)
    {
        _recovery = new SceneRecovery(this);
        Duration.Value = MediaTime.FromSeconds(300);
        ScanProperties<Scene>();
        FrameSize.Value = new PixelSize(width, height);
        Children.CollectionChanged += Children_CollectionChanged;
        Children.Attached += item => item.Edited += OnElementEdited;
        Children.Detached += item => item.Edited -= OnElementEdited;
        Layers.CollectionChanged += Layers_CollectionChanged;
        Layers.Attached += OnLayerAttached;
        Layers.Detached += OnLayerDetached;
        Name.Value = name;
    }

    internal SceneRecovery Recovery => _recovery;

    public event EventHandler? Edited;

    public IProperty<PixelSize> FrameSize { get; } = new ValueOnlyProperty<PixelSize>(default);

    // Serialize and Deserialize write and read the elements, groups and markers themselves.
    [NotAutoSerialized]
    public IListProperty<Element> Children { get; } = new ListProperty<Element> { AffectsRender = false };

    [Display(Name = nameof(Strings.StartTime), ResourceType = typeof(Strings))]
    public IProperty<MediaTime> Start { get; } = new ValueOnlyProperty<MediaTime>(MediaTime.Zero) { Coerce = static (_, value) => NotNegative(value) };

    [Display(Name = nameof(Strings.DurationTime), ResourceType = typeof(Strings))]
    public IProperty<MediaTime> Duration { get; } = new ValueOnlyProperty<MediaTime>(MediaTime.Zero) { Coerce = static (_, value) => NotNegative(value) };

    [NotAutoSerialized]
    public IListProperty<ImmutableHashSet<Guid>> Groups { get; } = new ListProperty<ImmutableHashSet<Guid>> { AffectsRender = false };

    public IListProperty<TimelineLayer> Layers { get; } = new ListProperty<TimelineLayer> { AffectsRender = false };

    [NotAutoSerialized]
    public IListProperty<SceneMarker> Markers { get; } = new ListProperty<SceneMarker> { AffectsRender = false };

    // Layers is saved before Id and Name, where its CoreProperty form was, and Deserialize reads it.
    internal override bool IsBaseField(IProperty property) => ReferenceEquals(property, Layers) || base.IsBaseField(property);

    private static MediaTime NotNegative(MediaTime value) => value < MediaTime.Zero ? MediaTime.Zero : value;

    public bool IsLayerLocked(int zIndex)
    {
        foreach (TimelineLayer layer in Layers)
        {
            if (layer.ZIndex.Value == zIndex && layer.IsLocked.Value) return true;
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
        return element.IsLocked.Value || IsLayerLocked(element.ZIndex.Value);
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

    public void MoveChild(int zIndex, MediaTime start, MediaTime length, Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (start < MediaTime.Zero)
            throw new ArgumentOutOfRangeException(nameof(start));

        if (length <= MediaTime.Zero)
            throw new ArgumentOutOfRangeException(nameof(length));

        MoveChildCore(zIndex, start, length, element);
    }

    public void MoveChildren(int deltaIndex, MediaTime deltaStart, Element[] elements)
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
            if (item is TimelineLayer { IsVideoMuted.Value: true } or TimelineLayer { IsAudioMuted.Value: true }
                or TimelineLayer { IsSolo.Value: true })
            {
                return true;
            }
        }

        return false;
    }

    private void OnLayerAttached(TimelineLayer layer)
    {
        layer.IsVideoMuted.ValueChanged += OnLayerFlagChanged;
        layer.IsAudioMuted.ValueChanged += OnLayerFlagChanged;
        layer.IsSolo.ValueChanged += OnLayerFlagChanged;
        layer.ZIndex.ValueChanged += OnLayerZIndexChanged;
    }

    private void OnLayerDetached(TimelineLayer layer)
    {
        layer.IsVideoMuted.ValueChanged -= OnLayerFlagChanged;
        layer.IsAudioMuted.ValueChanged -= OnLayerFlagChanged;
        layer.IsSolo.ValueChanged -= OnLayerFlagChanged;
        layer.ZIndex.ValueChanged -= OnLayerZIndexChanged;
    }

    // Edited triggers a preview re-render; Name/Color/IsLocked are editor-only
    // and must not. ZIndex retargets existing mute/solo flags, so it counts.
    private void OnLayerFlagChanged(object? sender, PropertyValueChangedEventArgs<bool> e)
        => Edited?.Invoke(e.Property.GetOwnerObject(), EventArgs.Empty);

    private void OnLayerZIndexChanged(object? sender, PropertyValueChangedEventArgs<int> e)
        => Edited?.Invoke(e.Property.GetOwnerObject(), EventArgs.Empty);

    private void OnElementEdited(object? sender, EventArgs e)
    {
        Edited?.Invoke(sender, e);
    }
}
