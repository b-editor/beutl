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

public class Element : Hierarchical, INotifyEdited
{
    public static readonly CoreProperty<TimeSpan> StartProperty;
    public static readonly CoreProperty<TimeSpan> LengthProperty;
    public static readonly CoreProperty<int> ZIndexProperty;
    public static readonly CoreProperty<Color> AccentColorProperty;
    public static readonly CoreProperty<bool> IsEnabledProperty;
    public static readonly CoreProperty<bool> IsLockedProperty;
    public static readonly CoreProperty<ICoreList<EngineObject>> ObjectsProperty;
    public static readonly CoreProperty<ClipTransition?> EnterTransitionProperty;
    public static readonly CoreProperty<ClipTransition?> ExitTransitionProperty;
    private readonly HierarchicalList<EngineObject> _objects;
    private TimeSpan _start;
    private TimeSpan _length;
    private int _zIndex;
    private bool _isEnabled = true;
    private bool _isLocked;
    private ClipTransition? _enterTransition;
    private ClipTransition? _exitTransition;

    static Element()
    {
        StartProperty = ConfigureProperty<TimeSpan, Element>(nameof(Start))
            .Accessor(o => o.Start, (o, v) => o.Start = v)
            .Register();

        LengthProperty = ConfigureProperty<TimeSpan, Element>(nameof(Length))
            .Accessor(o => o.Length, (o, v) => o.Length = v)
            .Register();

        ZIndexProperty = ConfigureProperty<int, Element>(nameof(ZIndex))
            .Accessor(o => o.ZIndex, (o, v) => o.ZIndex = v)
            .Register();

        AccentColorProperty = ConfigureProperty<Color, Element>(nameof(AccentColor))
            .DefaultValue(Colors.Teal)
            .Register();

        IsEnabledProperty = ConfigureProperty<bool, Element>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .Register();

        IsLockedProperty = ConfigureProperty<bool, Element>(nameof(IsLocked))
            .Accessor(o => o.IsLocked, (o, v) => o.IsLocked = v)
            .DefaultValue(false)
            .Register();

        ObjectsProperty = ConfigureProperty<ICoreList<EngineObject>, Element>(nameof(Objects))
            .Accessor(o => o.Objects)
            .Register();

        EnterTransitionProperty = ConfigureProperty<ClipTransition?, Element>(nameof(EnterTransition))
            .Accessor(o => o.EnterTransition, (o, v) => o.EnterTransition = v)
            .Register();

        ExitTransitionProperty = ConfigureProperty<ClipTransition?, Element>(nameof(ExitTransition))
            .Accessor(o => o.ExitTransition, (o, v) => o.ExitTransition = v)
            .Register();

        Hierarchy<Element>(EnterTransitionProperty, ExitTransitionProperty);
    }

    public Element()
    {
        _objects = new HierarchicalList<EngineObject>(this);
        _objects.Attached += OnObjectAttached;
        _objects.Detached += OnObjectDetached;
        _objects.CollectionChanged += OnObjectsCollectionChanged;
    }

    public event EventHandler? Edited;

    // 0以上
    [Display(Name = nameof(Strings.StartTime), ResourceType = typeof(Strings))]
    public TimeSpan Start
    {
        get => _start;
        set => SetAndRaise(StartProperty, ref _start, value);
    }

    [Display(Name = nameof(Strings.DurationTime), ResourceType = typeof(Strings))]
    public TimeSpan Length
    {
        get => _length;
        set => SetAndRaise(LengthProperty, ref _length, value);
    }

    public TimeRange Range => new(Start, Length);

    public int ZIndex
    {
        get => _zIndex;
        set => SetAndRaise(ZIndexProperty, ref _zIndex, value);
    }

    public Color AccentColor
    {
        get => GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetAndRaise(IsEnabledProperty, ref _isEnabled, value);
    }

    // Editor-only; does not affect composition. Timeline and property editors refuse
    // edits when this is true or its TimelineLayer.IsLocked is.
    public bool IsLocked
    {
        get => _isLocked;
        set => SetAndRaise(IsLockedProperty, ref _isLocked, value);
    }

    [NotAutoSerialized]
    public ICoreList<EngineObject> Objects => _objects;

    /// <summary>
    /// Gets or sets this element's side of the transition at its start: how it blends in from the adjacent
    /// element before it on the same layer, or from nothing when there is none.
    /// </summary>
    [NotAutoSerialized]
    [Display(Name = nameof(Strings.EnterTransition), ResourceType = typeof(Strings))]
    public ClipTransition? EnterTransition
    {
        get => _enterTransition;
        set => SetAndRaise(EnterTransitionProperty, ref _enterTransition, value);
    }

    /// <summary>
    /// Gets or sets this element's side of the transition at its end: how it blends out into the adjacent
    /// element after it on the same layer, or into nothing when there is none.
    /// </summary>
    [NotAutoSerialized]
    [Display(Name = nameof(Strings.ExitTransition), ResourceType = typeof(Strings))]
    public ClipTransition? ExitTransition
    {
        get => _exitTransition;
        set => SetAndRaise(ExitTransitionProperty, ref _exitTransition, value);
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
            if (ElementMigration.TryMigrateFromOperation(jsonContext, out EngineObject[] migrated))
            {
                Objects.Replace(migrated);
                context.ReportPersistedContentMigration(Project.DefaultMinAppVersion);
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

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);

        if (args is CorePropertyChangedEventArgs e)
        {
            TimeRange GetOldRange()
            {
                if (e.Property == StartProperty)
                {
                    return new TimeRange(((CorePropertyChangedEventArgs<TimeSpan>)args).OldValue, Length);
                }
                else if (e.Property == LengthProperty)
                {
                    return new TimeRange(Start, ((CorePropertyChangedEventArgs<TimeSpan>)args).OldValue);
                }
                else
                {
                    return default;
                }
            }

            if (e.Property == StartProperty || e.Property == LengthProperty)
            {
                // 全EngineObjectのTimeRangeを更新
                TimeRange newRange = Range;
                foreach (EngineObject obj in _objects)
                {
                    obj.TimeRange = newRange;
                }

                if (EnterTransition != null) EnterTransition.TimeRange = newRange;
                if (ExitTransition != null) ExitTransition.TimeRange = newRange;

                TimeRange oldRange = GetOldRange();
                Edited?.Invoke(this, new ElementEditedEventArgs
                {
                    AffectedRange = GetAffectedRanges(newRange, oldRange)
                });
            }
            else if (e.Property == ZIndexProperty)
            {
                foreach (EngineObject obj in _objects)
                {
                    obj.ZIndex = ZIndex;
                }

                Edited?.Invoke(this, EventArgs.Empty);
            }
            else if (e.Property == EnterTransitionProperty || e.Property == ExitTransitionProperty)
            {
                var change = (CorePropertyChangedEventArgs<ClipTransition?>)args;
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
            else if (e.Property == IsEnabledProperty)
            {
                Edited?.Invoke(this, EventArgs.Empty);
            }
        }
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
            foreach (Element? item in scene.Children.GetMarshal().Value)
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
            foreach (Element? item in scene.Children.GetMarshal().Value)
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

            foreach (Element? item in scene.Children.GetMarshal().Value)
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
