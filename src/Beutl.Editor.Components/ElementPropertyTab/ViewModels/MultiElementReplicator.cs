using System.Collections;
using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Infrastructure;
using Beutl.Editor.Observers;
using Beutl.Editor.Operations;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.ElementPropertyTab.ViewModels;

// Repeats each change of the copy that the element property tab shows on the elements it stands for, on the objects
// that stand where the changed one does (MultiElementCorrespondence). The element the copy was made from gets exact
// copies of new objects, Ids included, so that it stays equal to the copy; the others get copies with Ids of their own.
// A new item of a list goes next to the item that stands for its neighbour, so that it stands for the copy's new item.
internal sealed class MultiElementReplicator : IObserver<ChangeOperation>, IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<MultiElementReplicator>();
    private readonly Element _copy;
    private readonly Element _shown;
    private readonly IReadOnlyList<Element> _others;
    private readonly MultiElementCorrespondence _correspondence;
    private readonly Scene? _scene;
    private readonly IFrameCacheInvalidator? _invalidator;
    private readonly CoreObjectOperationObserver _observer;
    // After a change of a value of the copy that replaced an object: what each element held there, or null where it
    // held nothing. A group that takes a value's place takes the value in next, and each element then takes its own
    // value back rather than a copy of the copy's.
    private (CoreObject Copy, Dictionary<Element, CoreObject?> Targets)? _replaced;

    public MultiElementReplicator(
        MultiElementCorrespondence correspondence, Element shown, IReadOnlyList<Element> others, Scene? scene,
        IFrameCacheInvalidator? invalidator)
    {
        _copy = correspondence.Copy;
        _shown = shown;
        _others = others;
        _correspondence = correspondence;
        _scene = scene;
        _invalidator = invalidator;
        _observer = new CoreObjectOperationObserver(this, _copy, new OperationSequenceGenerator());
    }

    public Element Copy => _copy;

    public Element Shown => _shown;

    // While a command runs on the copy: the other elements that ran it themselves, on their own values, so that what it
    // changes of the copy does not reach them as well. An element no editor of its own could run it for, as when the
    // command edits an item of a list, takes what it changes instead.
    public IReadOnlyCollection<Element> RanThemselves { get; set; } = [];

    // Raised after a change of what objects the elements hold, which can change what the copy has in common with them.
    public event Action? StructureChanged;

    // The elements other than the one the copy was made from that an edit can change now.
    public IEnumerable<Element> EditableOthers() => _others.Where(IsEditable);

    // The object at the place of the copy's object in target, or null when target holds none there.
    public CoreObject? Find(CoreObject copyObject, Element target) => _correspondence.Find(copyObject, target);

    public void OnNext(ChangeOperation value)
    {
        try
        {
            Replicate(value);
        }
        catch (Exception ex)
        {
            // An edit of the copy runs inside its property's notification, which must not fail for this.
            s_logger.LogError(ex, "Could not repeat an edit of the selected elements on each of them.");
        }
    }

    public void OnError(Exception error)
    {
    }

    public void OnCompleted()
    {
    }

    public void Dispose()
    {
        _observer.Dispose();
    }

    // An object the copy owns becomes an object of the element's own, and so does a list a property holds as its value,
    // such as a pen's dashes, which an edit changes in place; a struct, a string or another value without identity is
    // shared, as is an object held from elsewhere, such as a scene.
    public object? CopyValue(object? value, bool exact)
    {
        switch (value)
        {
            case CoreObject obj when IsOwned(obj):
                return exact ? CopyExactly(obj) : CopyWithNewIds(obj);
            case SplineEasing easing:
                return new SplineEasing(easing.X1, easing.Y1, easing.X2, easing.Y2);
            case IList { IsFixedSize: false, IsReadOnly: false } list
                when value.GetType().GetConstructor(Type.EmptyTypes) is { } constructor:
                var copy = (IList)constructor.Invoke(null);
                foreach (object? item in list)
                {
                    copy.Add(CopyValue(item, exact));
                }

                return copy;
            default:
                return value;
        }
    }

    private void Replicate(ChangeOperation operation)
    {
        switch (operation)
        {
            case UpdateSplineEasingOperation easing:
                ReplicateEasing(easing);
                break;
            case ICollectionChangeOperation collection:
                ReplicateCollection(operation, collection);
                OnStructureChanged();
                break;
            case IUpdatePropertyValueOperation update:
                ReplicateUpdate(update);
                break;
            case UpdateNodeMemberOperation member:
                ReplicateNodeMember(member);
                break;
        }
    }

    private void OnStructureChanged()
    {
        _correspondence.Invalidate();
        StructureChanged?.Invoke();
    }

    private void ReplicateUpdate(IUpdatePropertyValueOperation update)
    {
        // The tab edits the element's objects; the element's own times and layer are the timeline's.
        if (ReferenceEquals(update.Object, _copy)) return;

        (string name, PropertyPathSlot slot) = PropertyPathHelper.Parse(update.PropertyPath);
        // An element gives its objects its own layer and time, as it does to those the copies put in it.
        if (update.Object is EngineObject && name is nameof(EngineObject.ZIndex) or nameof(EngineObject.TimeRange)) return;
        if (update.Object.Properties.FirstOrDefault(p => p.Name == name) is not { } source) return;

        // A reference is repeated through its expression; the value is the object it finds.
        if (slot == PropertyPathSlot.Value && source is IExpressionProperty { IsReference: true }) return;

        // A keyframe that moved is found where it was.
        bool movesKeyFrame = update.Object is IKeyFrame && name == nameof(IKeyFrame.KeyTime);
        bool reachesOthers = ReachesOthers(update.Object, null);
        var written = new List<Element>();
        var replaced = new Dictionary<Element, CoreObject?>();
        foreach ((Element target, bool exact) in Targets())
        {
            if (!exact && !reachesOthers) continue;

            CoreObject? owner = movesKeyFrame
                ? FindKeyFrame((IKeyFrame)update.Object, target, (MediaTime)update.OldValue!)
                : Find(update.Object, target);
            if (owner is null || MultiElementCorrespondence.FindProperty(owner, source) is not { } property) continue;

            switch (slot)
            {
                case PropertyPathSlot.Value:
                    replaced[target] = property.Value as CoreObject;
                    property.Value = CopyValue(update.NewValue, exact);
                    break;
                case PropertyPathSlot.Animation:
                    property.SetPropertyAnimation(CopyValue(update.NewValue, exact) as IPropertyAnimation);
                    break;
                case PropertyPathSlot.Expression:
                    CopyExpression(source, property, update.NewValue as IExpression, exact);
                    break;
            }

            written.Add(target);
        }

        _replaced = slot == PropertyPathSlot.Value && update.OldValue is CoreObject old ? (old, replaced) : null;

        // A new object, a new chain or a keyframe at another time changes what stands where.
        if (slot != PropertyPathSlot.Value || update.NewValue is CoreObject || update.OldValue is CoreObject || movesKeyFrame)
            OnStructureChanged();

        Invalidate(written);
    }

    // A member of a node, such as a layer input, keeps its value in its own adapter rather than in a property. Like any
    // edit inside a node graph, it reaches only the element the copy was made from (ReachesOthers).
    private void ReplicateNodeMember(UpdateNodeMemberOperation operation)
    {
        if (!IsEditable(_shown)
            || operation.NodeMember is not CoreObject member
            || Find(member, _shown) is not INodeMember { Property: { } property })
        {
            return;
        }

        switch (PropertyPathHelper.Parse(operation.PropertyPath).Slot)
        {
            case PropertyPathSlot.Value:
                property.SetValue(CopyValue(operation.NewValue, exact: true));
                break;
            case PropertyPathSlot.Animation when property is IAnimatablePropertyAdapter animatable:
                animatable.Animation = CopyValue(operation.NewValue, exact: true) as IKeyFrameAnimation;
                break;
            default:
                return;
        }

        Invalidate([_shown]);
    }

    // The keyframe of target that stands for the copy's keyframe when that was at time.
    private CoreObject? FindKeyFrame(IKeyFrame keyFrame, Element target, MediaTime time)
    {
        if ((keyFrame as IHierarchical)?.HierarchicalParent is not CoreObject animation
            || Find(animation, target) is not { } targetAnimation)
        {
            return null;
        }

        return targetAnimation.Properties.Select(p => p.Value).OfType<IList>()
            .Select(list => MultiElementCorrespondence.FindKeyFrame(list, time))
            .FirstOrDefault(found => found != null) as CoreObject;
    }

    private void ReplicateCollection(ChangeOperation operation, ICollectionChangeOperation collection)
    {
        string name = PropertyPathHelper.GetPropertyNameFromPath(collection.PropertyPath);
        if (collection.Object.Properties.FirstOrDefault(p => p.Name == name) is not { Value: IList list } source) return;

        Type type = operation.GetType();
        if (!type.IsGenericType) return;

        Type definition = type.GetGenericTypeDefinition();
        object?[] current = [.. list.Cast<object?>()];
        bool reachesOthers = ReachesOthers(collection.Object, source);
        var written = new List<Element>();
        foreach ((Element target, bool exact) in Targets())
        {
            if (!exact && !reachesOthers) continue;

            if (Find(collection.Object, target) is not { } owner
                || MultiElementCorrespondence.FindProperty(owner, source)?.Value is not IList targetList)
            {
                continue;
            }

            bool changed = false;
            if (definition == typeof(InsertCollectionRangeOperation<>))
            {
                changed = Insert(
                    collection.Object, source, current, Get<int>(operation, "Index"),
                    [.. Get<IEnumerable>(operation, "Items").Cast<object?>()], targetList, target, exact);
            }
            else if (definition == typeof(RemoveCollectionRangeOperation<>))
            {
                changed = Remove(
                    collection.Object, source, current, Get<int>(operation, "Index"),
                    [.. Get<IEnumerable>(operation, "Items").Cast<object?>()], targetList);
            }
            else if (definition == typeof(MoveCollectionRangeOperation<>))
            {
                changed = Move(
                    collection.Object, source, current, Get<int>(operation, "OldIndex"), Get<int>(operation, "NewIndex"),
                    Get<int>(operation, "Count"), targetList);
            }

            if (changed) written.Add(target);
        }

        Invalidate(written);
    }

    // Puts a copy of each new item next to the item that stands for its neighbour in the copy, the one before it or else
    // the one after it. Keyframes go where their time puts them. The element the copy was made from holds what the copy
    // held, so its items go where the copy's did, and so do values without identity, such as a pen's dashes, which stand
    // for those at their places.
    private bool Insert(
        CoreObject owner, IProperty source, object?[] current, int index, object?[] items, IList targetList, Element target,
        bool exact)
    {
        if (items.Length == 0) return false;

        bool changed = false;
        if (exact || items.All(IsPlain))
        {
            int position = Math.Clamp(index, 0, targetList.Count);
            foreach (object? item in items)
            {
                if (!TryGetValueFor(item, target, exact, out object? value)) continue;

                targetList.Insert(position++, value);
                changed = true;
            }

            return changed;
        }

        if (items.All(item => item is IKeyFrame))
        {
            foreach (object? item in items)
            {
                var keyFrame = (IKeyFrame)CopyValue(item, exact)!;
                int sorted = targetList.OfType<IKeyFrame>().TakeWhile(k => k.KeyTime.Value <= keyFrame.KeyTime.Value).Count();
                targetList.Insert(sorted, keyFrame);
            }

            return true;
        }

        // The list as it was before the items came.
        object?[] before = [.. current.Take(index), .. current.Skip(index + items.Length)];
        Dictionary<object, object> pairs = _correspondence.Pair(owner, source, before, targetList);
        int at = -1;
        for (int i = index - 1; i >= 0 && at < 0; i--)
        {
            if (before[i] is { } item && pairs.TryGetValue(item, out object? paired))
                at = targetList.IndexOf(paired) + 1;
        }

        for (int i = index; i < before.Length && at < 0; i++)
        {
            if (before[i] is { } item && pairs.TryGetValue(item, out object? paired))
                at = targetList.IndexOf(paired);
        }

        if (at < 0)
            at = index == 0 ? 0 : targetList.Count;

        foreach (object? item in items)
        {
            if (!TryGetValueFor(item, target, exact, out object? value)) continue;

            targetList.Insert(at++, value);
            changed = true;
        }

        return changed;
    }

    // What target takes for item, a new item of the copy: a copy of it, unless item is the copy's value that a change of
    // value just replaced, as when a group takes the place of a value and then takes the value in. Target then takes its
    // own value back, as its own editor would do, and nothing when it held none.
    private bool TryGetValueFor(object? item, Element target, bool exact, out object? value)
    {
        if (item is CoreObject obj && _replaced is { } replaced && ReferenceEquals(replaced.Copy, obj)
            && replaced.Targets.Remove(target, out CoreObject? own))
        {
            value = own;
            return own is not null;
        }

        value = CopyValue(item, exact);
        return true;
    }

    // A value without identity, such as a number, stands for the one at its place in the element's list.
    private static bool IsPlain(object? item) => item is not (null or CoreObject);

    // Removes the items that stand for the removed ones; keyframes are those at the removed ones' times, and values
    // without identity those at their places.
    private bool Remove(CoreObject owner, IProperty source, object?[] current, int index, object?[] items, IList targetList)
    {
        if (items.Length == 0) return false;

        if (items.All(IsPlain))
        {
            int count = Math.Min(items.Length, targetList.Count - index);
            for (int i = 0; i < count; i++)
            {
                targetList.RemoveAt(index);
            }

            return count > 0;
        }

        object?[] toRemove;
        if (items.All(item => item is IKeyFrame))
        {
            toRemove = [.. items.Cast<IKeyFrame>().Select(k => MultiElementCorrespondence.FindKeyFrame(targetList, k.KeyTime.Value))];
        }
        else
        {
            // The list as it was, with the items still in it.
            object?[] before = [.. current.Take(index), .. items, .. current.Skip(index)];
            Dictionary<object, object> pairs = _correspondence.Pair(owner, source, before, targetList);
            toRemove = [.. items.Select(item => item is not null ? pairs.GetValueOrDefault(item) : null)];
        }

        bool any = false;
        foreach (object? item in toRemove)
        {
            int at = item is null ? -1 : targetList.IndexOf(item);
            if (at < 0) continue;

            targetList.RemoveAt(at);
            any = true;
        }

        return any;
    }

    // Moves the items that stand for the moved ones next to the item that stands for their new neighbour in the copy,
    // the one before them or else the one after them. Keyframes keep the order of their times, which their own moves set.
    private bool Move(
        CoreObject owner, IProperty source, object?[] current, int oldIndex, int newIndex, int count, IList targetList)
    {
        if (targetList is not ICoreList coreList || count <= 0 || newIndex < 0 || newIndex + count > current.Length)
            return false;

        object?[] block = current[newIndex..(newIndex + count)];
        // A keyframe that moved in time moves itself in every element.
        if (block.All(item => item is IKeyFrame)) return false;

        // Values without identity move from their places to theirs. A move to a later place names it as the list has it
        // after the move, and MoveRange as it has it before.
        if (block.All(IsPlain))
        {
            if (oldIndex < 0 || oldIndex == newIndex || Math.Max(oldIndex, newIndex) + count > coreList.Count) return false;

            coreList.MoveRange(oldIndex, count, newIndex > oldIndex ? newIndex + count : newIndex);
            return true;
        }

        // The list as it was, with the block where it came from.
        object?[] rest = [.. current.Take(newIndex), .. current.Skip(newIndex + count)];
        int from = Math.Clamp(oldIndex, 0, rest.Length);
        object?[] before = [.. rest.Take(from), .. block, .. rest.Skip(from)];
        Dictionary<object, object> pairs = _correspondence.Pair(owner, source, before, targetList);
        object[] moved = [.. block.Where(item => item is not null && pairs.ContainsKey(item)).Select(item => pairs[item!])];
        if (moved.Length == 0) return false;

        object? after = null;
        for (int i = newIndex - 1; i >= 0 && after is null; i--)
        {
            if (current[i] is { } item && pairs.TryGetValue(item, out object? paired)) after = paired;
        }

        object? next = null;
        for (int i = newIndex + count; i < current.Length && after is null && next is null; i++)
        {
            if (current[i] is { } item && pairs.TryGetValue(item, out object? paired)) next = paired;
        }

        if (after is null && next is null) return false;

        bool changed = false;
        object? previous = after;
        foreach (object item in moved)
        {
            int at = targetList.IndexOf(item);
            int to;
            if (previous is not null)
            {
                int previousAt = targetList.IndexOf(previous);
                to = at > previousAt ? previousAt + 1 : previousAt;
            }
            else
            {
                int nextAt = targetList.IndexOf(next);
                to = at < nextAt ? nextAt - 1 : nextAt;
            }

            if (at >= 0 && to >= 0 && to != at)
            {
                coreList.Move(at, to);
                changed = true;
            }

            previous = item;
        }

        return changed;
    }

    private void ReplicateEasing(UpdateSplineEasingOperation operation)
    {
        if (operation.Parent is not { } keyFrame) return;

        string name = PropertyPathHelper.GetPropertyNameFromPath(operation.PropertyPath);
        bool reachesOthers = ReachesOthers(keyFrame, null);
        var written = new List<Element>();
        foreach ((Element target, bool exact) in Targets())
        {
            if ((!exact && !reachesOthers) || Find(keyFrame, target) is not KeyFrame { Easing.Value: SplineEasing easing })
                continue;

            switch (name)
            {
                case nameof(SplineEasing.X1):
                    easing.X1 = operation.NewValue;
                    break;
                case nameof(SplineEasing.Y1):
                    easing.Y1 = operation.NewValue;
                    break;
                case nameof(SplineEasing.X2):
                    easing.X2 = operation.NewValue;
                    break;
                case nameof(SplineEasing.Y2):
                    easing.Y2 = operation.NewValue;
                    break;
                default:
                    continue;
            }

            written.Add(target);
        }

        Invalidate(written);
    }

    // The keyframes and the expression over them, each a copy of the one in the copy's chain.
    private void CopyExpression(IProperty source, IProperty target, IExpression? chain, bool exact)
    {
        if (target is not IExpressionChainHost host) return;

        host.SetExpressionChain(CopyValue(ExpressionChain.FindKeyFrames(chain), exact) as IExpression);
        if (chain is not null and not IKeyFrameExpression
            && source is IExpressionProperty sourceProperty && sourceProperty.SerializeExpression() is { } node
            && target is IExpressionProperty targetProperty)
        {
            targetProperty.DeserializeExpression(node);
        }
    }

    // Held by an object of the copy, as opposed to a reference to one outside it.
    private bool IsOwned(CoreObject obj)
        => obj is not IHierarchical hierarchical
           || hierarchical.HierarchicalParent is null
           || ReferenceEquals(hierarchical.HierarchicalRoot, ((IHierarchical)_copy).HierarchicalRoot);

    private static CoreObject CopyExactly(CoreObject obj)
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(obj, new CoreSerializerOptions
        {
            Mode = CoreSerializationMode.EmbedReferencedObjects,
            EmbedsCopy = true
        });
        return (CoreObject)CoreSerializer.DeserializeFromJsonObject(json, obj.GetType());
    }

    private static CoreObject CopyWithNewIds(CoreObject obj)
    {
        ObjectRegenerator.Regenerate(obj, obj.GetType(), out ICoreSerializable copy);
        return (CoreObject)copy;
    }

    // Whether an edit of owner, or of its list, reaches the other elements: an edit of what the editors show of the copy
    // does, while one of what they hide stays with the element the copy was made from. The nodes and connections of a
    // node graph are hidden, and refer to each other by Id, which each element's copy of them would not share.
    private bool ReachesOthers(CoreObject owner, IProperty? list)
    {
        if (list is not null && owner is not IPropertyAnimation && !IsShown(owner, list)) return false;

        for (CoreObject current = owner; !ReferenceEquals(current, _copy);)
        {
            if ((current as IHierarchical)?.HierarchicalParent is not CoreObject parent) return false;

            // The keyframes of an animation show in the editor of the property it animates.
            if (parent is not IPropertyAnimation
                && (HoldingProperty(parent, current) is not { } holding || !IsShown(parent, holding)))
            {
                return false;
            }

            current = parent;
        }

        return true;
    }

    private static bool IsShown(CoreObject owner, IProperty property)
        => MultiElementCorrespondence.DisplayedProperties(owner).Contains(property);

    // The property of parent that holds child: as its value, among its items, or as its keyframes.
    private static IProperty? HoldingProperty(CoreObject parent, CoreObject child)
        => parent.Properties.FirstOrDefault(property =>
            ReferenceEquals(property.Value, child)
            || (property.Value is IList list && list.Cast<object?>().Any(item => ReferenceEquals(item, child)))
            || (property is IExpressionProperty && ReferenceEquals(property.GetPropertyAnimation(), child)));

    // The element the copy was made from first, with exact copies, then the others that did not run a command themselves.
    private IEnumerable<(Element Target, bool Exact)> Targets()
    {
        if (IsEditable(_shown)) yield return (_shown, true);

        foreach (Element other in _others)
        {
            if (IsEditable(other) && !RanThemselves.Contains(other)) yield return (other, false);
        }
    }

    // A removed element is no longer recorded for undo, though the selection lets go of it only after.
    private bool IsEditable(Element element)
        => element.HierarchicalParent is Scene && ElementEditability.IsEditable(element, _scene);

    private void Invalidate(List<Element> written)
    {
        if (_invalidator is null || written.Count == 0) return;

        TimeRange range = written[0].Range;
        foreach (Element element in written.Skip(1))
        {
            range = range.Union(element.Range);
        }

        _invalidator.Invalidate(range);
    }

    private static T Get<T>(object operation, string name) => (T)operation.GetType().GetProperty(name)!.GetValue(operation)!;
}
