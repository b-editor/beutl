using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel.DataAnnotations;
using Beutl.Collections;
using Beutl.Collections.Pooled;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Transitions;
using Beutl.JsonConverters;
using Beutl.Language;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.ProjectSystem;

// A Beutl 1.x element kept its operators and its node tree under these keys. The migration reads them, and a 2.x
// element drops them rather than keeping them as unknown members.
[DroppedMembers("Operation", "UseNode", "NodeTree")]
public partial class Element : Hierarchical, INotifyEdited
{
    public Element()
    {
        ScanProperties<Element>();
        Start.ValueChanged += (_, e) => OnRangeChanged(new TimeRange(e.OldValue, Length.Value));
        Length.ValueChanged += (_, e) => OnRangeChanged(new TimeRange(Start.Value, e.OldValue));
        ZIndex.ValueChanged += (_, _) =>
        {
            (HierarchicalParent as Scene)?.UpdateElementIndex(this);
            foreach (EngineObject obj in Objects)
            {
                obj.ZIndex.Value = ZIndex.Value;
            }

            Edited?.Invoke(this, EventArgs.Empty);
        };
        IsEnabled.ValueChanged += (_, _) => Edited?.Invoke(this, EventArgs.Empty);
        MatteSourceKind.ValueChanged += (_, _) => Edited?.Invoke(this, EventArgs.Empty);
        MatteSourceZIndex.ValueChanged += (_, _) => Edited?.Invoke(this, EventArgs.Empty);
        MatteMode.ValueChanged += (_, _) => Edited?.Invoke(this, EventArgs.Empty);
        IsVisibleInOutput.ValueChanged += (_, _) => Edited?.Invoke(this, EventArgs.Empty);
        EnterTransition.ValueChanged += (_, e) => OnTransitionChanged(e);
        ExitTransition.ValueChanged += (_, e) => OnTransitionChanged(e);
        Objects.Attached += OnObjectAttached;
        Objects.Detached += OnObjectDetached;
        Objects.CollectionChanged += OnObjectsCollectionChanged;
    }

    public event EventHandler? Edited;

    [Display(Name = nameof(Strings.StartTime), ResourceType = typeof(Strings))]
    public IProperty<MediaTime> Start { get; } = Property.CreateValue(MediaTime.Zero);

    [Display(Name = nameof(Strings.DurationTime), ResourceType = typeof(Strings))]
    public IProperty<MediaTime> Length { get; } = Property.CreateValue(MediaTime.Zero);

    public IProperty<int> ZIndex { get; } = Property.CreateValue(0);

    public IProperty<Color> AccentColor { get; } = Property.CreateValue(Colors.Teal);

    public IProperty<bool> IsEnabled { get; } = Property.CreateValue(true);

    public IProperty<MatteSourceKind> MatteSourceKind { get; } = Property.CreateValue(ProjectSystem.MatteSourceKind.Above);

    /// <summary>The explicit source layer, used when MatteSourceKind is Layer.</summary>
    public IProperty<int?> MatteSourceZIndex { get; } = Property.CreateValue<int?>(null);

    public IProperty<MatteMode> MatteMode { get; } = Property.CreateValue(Graphics.MatteMode.None);

    /// <summary>Whether to draw this element in the scene. Hidden elements can still supply a matte.</summary>
    public IProperty<bool> IsVisibleInOutput { get; } = Property.CreateValue(true);

    // Editor-only; does not affect composition. Timeline and property editors refuse
    // edits when this is true or its TimelineLayer.IsLocked is.
    public IProperty<bool> IsLocked { get; } = Property.CreateValue(false);

    // Serialize and Deserialize write and read the objects and transitions themselves.
    [NotAutoSerialized]
    public IListProperty<EngineObject> Objects { get; } = new ListProperty<EngineObject> { AffectsRender = false };

    /// <summary>
    /// Gets or sets this element's side of the transition at its start: how it blends in from the adjacent
    /// element before it on the same layer, or from nothing when there is none.
    /// </summary>
    [NotAutoSerialized]
    [Display(Name = nameof(Strings.EnterTransition), ResourceType = typeof(Strings))]
    public IProperty<ClipTransition?> EnterTransition { get; } = Property.CreateValue<ClipTransition?>(null);

    /// <summary>
    /// Gets or sets this element's side of the transition at its end: how it blends out into the adjacent
    /// element after it on the same layer, or into nothing when there is none.
    /// </summary>
    [NotAutoSerialized]
    [Display(Name = nameof(Strings.ExitTransition), ResourceType = typeof(Strings))]
    public IProperty<ClipTransition?> ExitTransition { get; } = Property.CreateValue<ClipTransition?>(null);

    public TimeRange Range => new(Start.Value, Length.Value);

    /// <summary>Resolves relative sources to the nearest clip layer at the frame's time.</summary>
    public int? GetMatteSourceZIndex(Scene scene, MediaTime time)
    {
        if (MatteSourceKind.Value == ProjectSystem.MatteSourceKind.Layer) return MatteSourceZIndex.Value;

        int? nearest = null;
        using var candidates = new PooledList<Element>();
        MediaTime tolerance = ElementTransitions.AdjacencyTolerance;
        scene.ElementIndex.Query(time - tolerance, time + tolerance, candidates);
        foreach (Element candidate in candidates)
        {
            int z = candidate.ZIndex.Value;
            bool closer = MatteSourceKind.Value switch
            {
                ProjectSystem.MatteSourceKind.Above => z < ZIndex.Value && (nearest == null || z > nearest),
                ProjectSystem.MatteSourceKind.Below => z > ZIndex.Value && (nearest == null || z < nearest),
                _ => false,
            };
            if (!closer || !candidate.CoversGraphicsTime(time)) continue;
            nearest = z;
        }

        return nearest;
    }

    // A transition can bridge the small gap tolerated between adjacent clips.
    internal bool CoversGraphicsTime(MediaTime time)
    {
        if (Range.Contains(time)) return true;
        MediaTime tolerance = ElementTransitions.AdjacencyTolerance;
        return time >= Start.Value - tolerance && time < Range.End + tolerance
               && ElementTransitions.TryGetActive(this, time, out _);
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

    public void NotifySplitted(bool backward, MediaTime startDelta, MediaTime durationDelta)
    {
        foreach (EngineObject item in Objects)
        {
            if (item is ISplittable splittable)
                splittable.NotifySplitted(backward, startDelta, durationDelta);
        }
    }

    public bool HasOriginalDuration()
    {
        foreach (EngineObject item in Objects)
        {
            if (item is IOriginalDurationProvider provider && provider.HasOriginalDuration())
                return true;
        }

        return false;
    }

    public bool TryGetOriginalDuration(out MediaTime timeSpan)
    {
        foreach (EngineObject item in Objects)
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
        context.SetValue(nameof(Length), MediaTimeJsonConverter.LengthToWrite(Start.Value, Length.Value));
        context.SetValue(nameof(Objects), Objects);
        if (EnterTransition.Value != null)
        {
            context.SetValue(nameof(EnterTransition), EnterTransition.Value);
        }

        if (ExitTransition.Value != null)
        {
            context.SetValue(nameof(ExitTransition), ExitTransition.Value);
        }
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        // Earlier track mattes only had an explicit layer, including null for no source.
        if (!context.Contains(nameof(MatteSourceKind)) && context.Contains(nameof(MatteSourceZIndex)))
            MatteSourceKind.Value = ProjectSystem.MatteSourceKind.Layer;
        Length.Value = MediaTimeJsonConverter.LengthRead(context, nameof(Start), nameof(Length), Start.Value, Length.Value);
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

        EnterTransition.Value = context.GetValue<ClipTransition>(nameof(EnterTransition));
        ExitTransition.Value = context.GetValue<ClipTransition>(nameof(ExitTransition));
    }

    public void CollectObjects(CompositionTarget target, IList<EngineObject> objects)
    {
        foreach (EngineObject obj in Objects)
        {
            if (!obj.IsEnabled.Value) continue;
            CompositionTarget t = obj.GetCompositionTarget();
            if (t != CompositionTarget.Unknown && t != target) continue;
            objects.Add(obj);
        }
    }

    private void OnRangeChanged(TimeRange oldRange)
    {
        // Transition invalidation below queries the new range before Edited is raised.
        (HierarchicalParent as Scene)?.UpdateElementIndex(this);
        TimeRange newRange = Range;
        foreach (EngineObject obj in Objects)
        {
            obj.TimeRange.Value = newRange;
        }

        if (EnterTransition.Value != null) EnterTransition.Value.TimeRange.Value = newRange;
        if (ExitTransition.Value != null) ExitTransition.Value.TimeRange.Value = newRange;

        Edited?.Invoke(this, new ElementEditedEventArgs
        {
            AffectedRange = GetAffectedRanges(newRange, oldRange)
        });
    }

    private void OnTransitionChanged(PropertyValueChangedEventArgs<ClipTransition?> change)
    {
        (HierarchicalParent as Scene)?.UpdateTransitionElement(this);
        if (change.OldValue != null)
        {
            change.OldValue.Edited -= OnObjectEdited;
        }

        if (change.NewValue != null)
        {
            // Animations evaluate against the owner's range, as they do for the element's objects.
            // The transition is not made a time anchor, so its range is not saved under the key its
            // own Duration property uses.
            change.NewValue.TimeRange.Value = Range;
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
            ranges.AddRange(ElementTransitions.GetRegionsNear(scene, newRange, ZIndex.Value));
            ranges.AddRange(ElementTransitions.GetRegionsNear(scene, oldRange, ZIndex.Value));
        }

        return ranges.DrainToImmutable();
    }

    private void UpdateObjectFromElement(EngineObject obj)
    {
        obj.IsTimeAnchor.Value = true;
        obj.ZIndex.Value = ZIndex.Value;
        obj.TimeRange.Value = new TimeRange(Start.Value, Length.Value);
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

    internal Element? GetBefore(int zindex, MediaTime start)
    {
        if (HierarchicalParent is Scene scene)
        {
            Element? tmp = null;
            foreach (Element? item in scene.Children.AsSpan())
            {
                if (item != this && item.ZIndex.Value == zindex && item.Start.Value < start)
                {
                    if (tmp == null || tmp.Start.Value <= item.Start.Value)
                    {
                        tmp = item;
                    }
                }
            }
            return tmp;
        }

        return null;
    }

    internal Element? GetAfter(int zindex, MediaTime end)
    {
        if (HierarchicalParent is Scene scene)
        {
            Element? tmp = null;
            foreach (Element? item in scene.Children.AsSpan())
            {
                if (item != this && item.ZIndex.Value == zindex && item.Range.End > end)
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

    internal (Element? Before, Element? After, Element? Cover) GetBeforeAndAfterAndCover(int zindex, MediaTime start, MediaTime end)
    {
        if (HierarchicalParent is Scene scene)
        {
            Element? beforeTmp = null;
            Element? afterTmp = null;
            Element? coverTmp = null;
            var range = TimeRange.FromRange(start, end);

            foreach (Element? item in scene.Children.AsSpan())
            {
                if (item != this && item.ZIndex.Value == zindex)
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
        if (item.Start.Value < range.Start
            && (before == null || before.Start.Value <= item.Start.Value))
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
