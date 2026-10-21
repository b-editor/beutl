using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Beutl.Collections;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Transitions;
using Beutl.Language;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.ProjectSystem;

// A Beutl 1.x element kept its operators and its node tree under these keys. The migration reads them, and a 2.x
// element drops them rather than keeping them as unknown members.
[DroppedMembers("Operation", "UseNode", "NodeTree")]
public class Element : Hierarchical, INotifyEdited
{
    private readonly HierarchicalList<EngineObject> _objects;

    public Element()
    {
        _objects = new HierarchicalList<EngineObject>(this);
        // The list is a value rather than a default, as its CoreProperty form had no default.
        ObjectsProperty.Value = _objects;
        ScanProperties<Element>();
        StartProperty.ValueChanged += (_, e) => OnRangeChanged(nameof(Start), new TimeRange(e.OldValue, Length));
        LengthProperty.ValueChanged += (_, e) => OnRangeChanged(nameof(Length), new TimeRange(Start, e.OldValue));
        ZIndexProperty.ValueChanged += (_, _) =>
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(ZIndex)));
            foreach (EngineObject obj in _objects)
            {
                obj.ZIndex = ZIndex;
            }

            Edited?.Invoke(this, EventArgs.Empty);
        };
        AccentColorProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(AccentColor)));
        IsEnabledProperty.ValueChanged += (_, _) =>
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsEnabled)));
            Edited?.Invoke(this, EventArgs.Empty);
        };
        IsLockedProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsLocked)));
        EnterTransitionProperty.ValueChanged += (_, e) => OnTransitionChanged(nameof(EnterTransition), e);
        ExitTransitionProperty.ValueChanged += (_, e) => OnTransitionChanged(nameof(ExitTransition), e);
        _objects.Attached += OnObjectAttached;
        _objects.Detached += OnObjectDetached;
        _objects.CollectionChanged += OnObjectsCollectionChanged;
    }

    public event EventHandler? Edited;

    [Display(Name = nameof(Strings.StartTime), ResourceType = typeof(Strings))]
    public IProperty<TimeSpan> StartProperty { get; } = new ValueOnlyProperty<TimeSpan>(TimeSpan.Zero);

    [Display(Name = nameof(Strings.DurationTime), ResourceType = typeof(Strings))]
    public IProperty<TimeSpan> LengthProperty { get; } = new ValueOnlyProperty<TimeSpan>(TimeSpan.Zero);

    public IProperty<int> ZIndexProperty { get; } = new ValueOnlyProperty<int>(0);

    public IProperty<Color> AccentColorProperty { get; } = new ValueOnlyProperty<Color>(Colors.Teal);

    public IProperty<bool> IsEnabledProperty { get; } = new ValueOnlyProperty<bool>(true);

    // Editor-only; does not affect composition. Timeline and property editors refuse
    // edits when this is true or its TimelineLayer.IsLocked is.
    public IProperty<bool> IsLockedProperty { get; } = new ValueOnlyProperty<bool>(false);

    // Serialize and Deserialize write and read the objects and transitions themselves.
    [NotAutoSerialized]
    public IProperty<ICoreList<EngineObject>> ObjectsProperty { get; } = new ValueOnlyProperty<ICoreList<EngineObject>>(null!);

    [NotAutoSerialized]
    [Display(Name = nameof(Strings.EnterTransition), ResourceType = typeof(Strings))]
    public IProperty<ClipTransition?> EnterTransitionProperty { get; } = new ValueOnlyProperty<ClipTransition?>(null);

    [NotAutoSerialized]
    [Display(Name = nameof(Strings.ExitTransition), ResourceType = typeof(Strings))]
    public IProperty<ClipTransition?> ExitTransitionProperty { get; } = new ValueOnlyProperty<ClipTransition?>(null);

    public TimeSpan Start
    {
        get => StartProperty.Value;
        set => StartProperty.Value = value;
    }

    public TimeSpan Length
    {
        get => LengthProperty.Value;
        set => LengthProperty.Value = value;
    }

    public TimeRange Range => new(Start, Length);

    public int ZIndex
    {
        get => ZIndexProperty.Value;
        set => ZIndexProperty.Value = value;
    }

    public Color AccentColor
    {
        get => AccentColorProperty.Value;
        set => AccentColorProperty.Value = value;
    }

    public bool IsEnabled
    {
        get => IsEnabledProperty.Value;
        set => IsEnabledProperty.Value = value;
    }

    public bool IsLocked
    {
        get => IsLockedProperty.Value;
        set => IsLockedProperty.Value = value;
    }

    // True while recovery keeps this element's original file because saving would lose part of
    // what it was read with, for example a keyframe easing from a plugin that is not installed (an
    // object whose type is missing does not count: its fallback saves what it was read with).
    // Saving leaves that file as it is, so the editor treats the element as read-only, except for
    // removing the element or the content that could not be loaded. A completed repair clears it;
    // undoing the repair sets it again.
    internal bool IsRecoveryProtected => SuppressedStorageSource is not null;

    // Raised after IsRecoveryProtected changes.
    internal event EventHandler? RecoveryProtectionChanged;

    internal override void OnStorageSuppressionChanged()
    {
        base.OnStorageSuppressionChanged();
        RecoveryProtectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public ICoreList<EngineObject> Objects => _objects;

    /// <summary>
    /// Gets or sets this element's side of the transition at its start: how it blends in from the adjacent
    /// element before it on the same layer, or from nothing when there is none.
    /// </summary>
    public ClipTransition? EnterTransition
    {
        get => EnterTransitionProperty.Value;
        set => EnterTransitionProperty.Value = value;
    }

    /// <summary>
    /// Gets or sets this element's side of the transition at its end: how it blends out into the adjacent
    /// element after it on the same layer, or into nothing when there is none.
    /// </summary>
    public ClipTransition? ExitTransition
    {
        get => ExitTransitionProperty.Value;
        set => ExitTransitionProperty.Value = value;
    }

    public void AddObject(EngineObject obj)
    {
        if (obj is IFlowOperator)
        {
            Objects.Add(new PortalObject());
        }
        Objects.Add(obj);
    }

    public void InsertObject(int index, EngineObject obj)
    {
        if (obj is IFlowOperator)
        {
            Objects.Insert(index, new PortalObject());
            Objects.Insert(index + 1, obj);
        }
        else
        {
            Objects.Insert(index, obj);
        }
    }

    public void RemoveObject(EngineObject obj)
    {
        Objects.Remove(obj);
    }

    public void NotifySplitted(bool backward, TimeSpan startDelta, TimeSpan durationDelta)
    {
        foreach (EngineObject item in _objects)
        {
            if (item is ISplittable splittable)
                splittable.NotifySplitted(backward, startDelta, durationDelta);
        }
    }

    public bool HasOriginalDuration()
    {
        foreach (EngineObject item in _objects)
        {
            if (item is IOriginalDurationProvider provider && provider.HasOriginalDuration())
                return true;
        }

        return false;
    }

    public bool TryGetOriginalDuration(out TimeSpan timeSpan)
    {
        foreach (EngineObject item in _objects)
        {
            if (item is IOriginalDurationProvider provider && provider.TryGetOriginalDuration(out timeSpan))
                return true;
        }

        timeSpan = default;
        return false;
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Objects), Objects);
        if (EnterTransition != null)
        {
            context.SetValue(nameof(EnterTransition), EnterTransition);
        }

        if (ExitTransition != null)
        {
            context.SetValue(nameof(ExitTransition), ExitTransition);
        }
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<EngineObject[]>(nameof(Objects)) is { } objects)
        {
            Objects.Replace(objects);
        }
        else if (context is IJsonSerializationContext jsonContext)
        {
            bool isLegacy = ElementMigration.TryMigrateFromOperation(jsonContext, out EngineObject[] migrated);
            if (ElementMigration.TryCreateUnmigratedNodeTree(jsonContext, out FallbackEngineObject? nodeTree))
            {
                migrated = [nodeTree, .. migrated];
                isLegacy = true;
            }

            if (isLegacy)
            {
                Objects.Replace(migrated);
            }
        }

        EnterTransition = context.GetValue<ClipTransition>(nameof(EnterTransition));
        ExitTransition = context.GetValue<ClipTransition>(nameof(ExitTransition));
    }

    public void CollectObjects(CompositionTarget target, IList<EngineObject> objects)
    {
        foreach (EngineObject obj in _objects)
        {
            if (!obj.IsEnabled) continue;
            CompositionTarget t = obj.GetCompositionTarget();
            if (t != CompositionTarget.Unknown && t != target) continue;
            objects.Add(obj);
        }
    }

    private void OnRangeChanged(string propertyName, TimeRange oldRange)
    {
        OnPropertyChanged(new PropertyChangedEventArgs(propertyName));
        TimeRange newRange = Range;
        foreach (EngineObject obj in _objects)
        {
            obj.TimeRange = newRange;
        }

        if (EnterTransition != null) EnterTransition.TimeRange = newRange;
        if (ExitTransition != null) ExitTransition.TimeRange = newRange;

        Edited?.Invoke(this, new ElementEditedEventArgs
        {
            AffectedRange = GetAffectedRanges(newRange, oldRange)
        });
    }

    private void OnTransitionChanged(string propertyName, PropertyValueChangedEventArgs<ClipTransition?> change)
    {
        OnPropertyChanged(new PropertyChangedEventArgs(propertyName));
        if (change.OldValue != null)
        {
            change.OldValue.Edited -= OnObjectEdited;
        }

        if (change.NewValue != null)
        {
            // Animations evaluate against the owner's range, as they do for the element's objects.
            // The transition is not made a time anchor, so its range is not saved under the key its
            // own Duration property uses.
            change.NewValue.TimeRange = Range;
            change.NewValue.Edited += OnObjectEdited;
        }

        Edited?.Invoke(this, EventArgs.Empty);
    }

    // A transition draws this element past its own range and the adjacent element past theirs, so moving
    // it changes the frames of every transition it took part in before and takes part in after.
    private ImmutableArray<TimeRange> GetAffectedRanges(TimeRange newRange, TimeRange oldRange)
    {
        ImmutableArray<TimeRange>.Builder ranges = ImmutableArray.CreateBuilder<TimeRange>(4);
        ranges.Add(newRange);
        ranges.Add(oldRange);
        if (HierarchicalParent is Scene scene)
        {
            ranges.AddRange(ElementTransitions.GetRegionsNear(scene, newRange));
            ranges.AddRange(ElementTransitions.GetRegionsNear(scene, oldRange));
        }

        return ranges.DrainToImmutable();
    }

    private void UpdateObjectFromElement(EngineObject obj)
    {
        obj.IsTimeAnchor = true;
        obj.ZIndex = ZIndex;
        obj.TimeRange = new TimeRange(Start, Length);
    }

    private void OnObjectAttached(EngineObject obj)
    {
        obj.Edited += OnObjectEdited;
        UpdateObjectFromElement(obj);
    }

    private void OnObjectDetached(EngineObject obj)
    {
        obj.Edited -= OnObjectEdited;
    }

    private void OnObjectsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Edited?.Invoke(this, EventArgs.Empty);
    }

    private void OnObjectEdited(object? sender, EventArgs e)
    {
        Edited?.Invoke(sender, e);
    }

    internal Element? GetBefore(int zindex, TimeSpan start)
    {
        if (HierarchicalParent is Scene scene)
        {
            Element? tmp = null;
            foreach (Element? item in scene.Children.AsSpan())
            {
                if (item != this && item.ZIndex == zindex && item.Start < start)
                {
                    if (tmp == null || tmp.Start <= item.Start)
                    {
                        tmp = item;
                    }
                }
            }
            return tmp;
        }

        return null;
    }

    internal Element? GetAfter(int zindex, TimeSpan end)
    {
        if (HierarchicalParent is Scene scene)
        {
            Element? tmp = null;
            foreach (Element? item in scene.Children.AsSpan())
            {
                if (item != this && item.ZIndex == zindex && item.Range.End > end)
                {
                    if (tmp == null || tmp.Range.End >= item.Range.End)
                    {
                        tmp = item;
                    }
                }
            }
            return tmp;
        }

        return null;
    }

    internal (Element? Before, Element? After, Element? Cover) GetBeforeAndAfterAndCover(int zindex, TimeSpan start, TimeSpan end)
    {
        if (HierarchicalParent is Scene scene)
        {
            Element? beforeTmp = null;
            Element? afterTmp = null;
            Element? coverTmp = null;
            var range = TimeRange.FromRange(start, end);

            foreach (Element? item in scene.Children.AsSpan())
            {
                if (item != this && item.ZIndex == zindex)
                {
                    AccumulateNeighbor(item, range, ref beforeTmp, ref afterTmp, ref coverTmp);
                }
            }
            return (beforeTmp, afterTmp, coverTmp);
        }

        return (null, null, null);
    }

    internal (Element? Before, Element? After, Element? Cover) GetBeforeAndAfterAndCover(int zindex, TimeSpan start, Element[] excludes)
    {
        if (HierarchicalParent is Scene scene)
        {
            Element? beforeTmp = null;
            Element? afterTmp = null;
            Element? coverTmp = null;
            var range = new TimeRange(start, Length);

            foreach (Element? item in scene.Children.Except(excludes))
            {
                if (item != this && item.ZIndex == zindex)
                {
                    AccumulateNeighbor(item, range, ref beforeTmp, ref afterTmp, ref coverTmp);
                }
            }
            return (beforeTmp, afterTmp, coverTmp);
        }

        return (null, null, null);
    }

    // Folds one element of the same layer into the nearest neighbors before and after range and the
    // element range covers.
    internal static void AccumulateNeighbor(
        Element item,
        TimeRange range,
        ref Element? before,
        ref Element? after,
        ref Element? cover)
    {
        if (item.Start < range.Start
            && (before == null || before.Start <= item.Start))
        {
            before = item;
        }

        if (item.Range.End > range.End
            && (after == null || after.Range.End >= item.Range.End))
        {
            after = item;
        }

        if (range.Contains(item.Range) || range == item.Range)
        {
            cover = item;
        }
    }
}
