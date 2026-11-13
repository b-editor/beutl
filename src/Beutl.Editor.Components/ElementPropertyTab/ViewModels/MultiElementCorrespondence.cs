using System.Collections;
using System.Runtime.CompilerServices;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.ElementPropertyTab.ViewModels;

// Which object of an element stands where each object of the copy does, the copy being what the element property tab
// shows for several elements. An object a property holds stands for the one the element's same property holds, when it
// is of the same kind; an item of a list stands for the item of its type at its place among the items of that type in
// the element's list, the first blur for the first blur; a keyframe stands for the one at its time. The element's own
// objects that find none of their type pair by kind, so that the drawable of one element stands for that of another
// even when one shows text and the other a shape: what both have in common, such as where they are drawn, can then be
// edited together.
internal sealed class MultiElementCorrespondence(Element copy)
{
    // What Find found since the copy or an element last changed in what objects they hold. What it did not find is
    // looked for again: a property tells of a new value before the value joins its owner, so an editor made then sees
    // an object of the copy that belongs to nothing yet.
    private readonly Dictionary<(CoreObject, Element), CoreObject> _found = new(PairComparer.Instance);

    public Element Copy => copy;

    // Forgets what was found, after a change of what objects the copy or an element holds.
    public void Invalidate() => _found.Clear();

    // The object of target at the place of obj, an object under the copy, or null when target holds none there.
    public CoreObject? Find(CoreObject obj, Element target)
    {
        if (ReferenceEquals(obj, copy)) return target;
        if (_found.TryGetValue((obj, target), out CoreObject? found) && IsUnder(found, target))
            return found;

        found = FindCore(obj, target);
        if (found is not null)
            _found[(obj, target)] = found;
        else
            _found.Remove((obj, target));

        return found;
    }

    // The property of targetOwner that stands for property: the one with its name and type.
    public static IProperty? FindProperty(CoreObject targetOwner, IProperty property)
        => targetOwner.Properties.FirstOrDefault(p => p.Name == property.Name && p.ValueType == property.ValueType);

    // Like FindProperty, among the properties an editor shows.
    public static IProperty? FindDisplayedProperty(CoreObject targetOwner, IProperty property)
        => DisplayedProperties(targetOwner).FirstOrDefault(p => p.Name == property.Name && p.ValueType == property.ValueType);

    // The properties an editor shows of obj.
    public static IEnumerable<IProperty> DisplayedProperties(CoreObject obj)
        => obj is EngineObject engineObject ? engineObject.GetDisplayProperties() : obj.Properties;

    // Pairs the items of a list of the copy, as copyItems has them, with those of the target's list at its place. The
    // list is a property of owner; copyItems can differ from what the list holds now, as when an item has just left it.
    public Dictionary<object, object> Pair(CoreObject owner, IProperty listProperty, IReadOnlyList<object?> copyItems, IList targetList)
        => Pair(copyItems, targetList, ByKind(owner, listProperty));

    // Whether the items of a list of owner are paired by kind: the element's objects are, the items of other lists by type.
    public static bool ByKind(CoreObject owner, IProperty listProperty)
        => owner is Element && listProperty.Name == nameof(Element.Objects);

    // Whether two objects are of one kind: of one type, or of types that share a base below EngineObject, as a text and a
    // shape share Drawable. Such objects share the properties of that base.
    public static bool SameKind(CoreObject a, CoreObject b)
    {
        Type typeA = a.GetType();
        Type typeB = b.GetType();
        if (typeA == typeB) return true;

        for (Type? type = typeA.BaseType; type != null; type = type.BaseType)
        {
            if (type == typeof(EngineObject) || !type.IsSubclassOf(typeof(EngineObject))) return false;
            if (type.IsAssignableFrom(typeB)) return true;
        }

        return false;
    }

    private CoreObject? FindCore(CoreObject obj, Element target)
    {
        if ((obj as IHierarchical)?.HierarchicalParent is not CoreObject parent
            || Find(parent, target) is not { } targetParent)
        {
            return null;
        }

        foreach (IProperty property in parent.Properties)
        {
            if (property is IExpressionProperty && ReferenceEquals(property.GetPropertyAnimation(), obj))
                return FindProperty(targetParent, property)?.GetPropertyAnimation() as CoreObject;

            switch (property.Value)
            {
                case CoreObject value when ReferenceEquals(value, obj):
                    return FindProperty(targetParent, property)?.Value is CoreObject targetValue
                           && IsHeldBy(targetValue, targetParent)
                           && SameKind(obj, targetValue)
                        ? targetValue
                        : null;

                case IList list when Contains(list, obj):
                    if (FindProperty(targetParent, property)?.Value is not IList targetList) return null;

                    if (obj is IKeyFrame keyFrame)
                        return FindKeyFrame(targetList, keyFrame.KeyTime.Value) as CoreObject;

                    return Pair([.. list.Cast<object?>()], targetList, ByKind(parent, property))
                        .GetValueOrDefault(obj) as CoreObject;
            }
        }

        return null;
    }

    // The keyframe at time in a list of keyframes.
    public static IKeyFrame? FindKeyFrame(IList keyFrames, MediaTime time)
        => keyFrames.OfType<IKeyFrame>().FirstOrDefault(k => k.KeyTime.Value == time);

    // Pairs the items of one type in the order each list holds them, the first of a type with the first of that type,
    // whatever lies between them; where byKind is set, the items left then pair with those left of one kind, in order
    // too. Keyframes pair by time instead.
    private static Dictionary<object, object> Pair(IReadOnlyList<object?> source, IList target, bool byKind)
    {
        var pairs = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        List<CoreObject> left = [.. target.OfType<CoreObject>().Where(item => item is not IKeyFrame)];
        PairInOrder(source, left, pairs, (a, b) => a.GetType() == b.GetType());
        if (byKind) PairInOrder(source, left, pairs, SameKind);
        return pairs;
    }

    // Pairs each item of source not paired yet with the first item of left that matches it, which then leaves left.
    private static void PairInOrder(
        IReadOnlyList<object?> source, List<CoreObject> left, Dictionary<object, object> pairs,
        Func<CoreObject, CoreObject, bool> match)
    {
        foreach (object? item in source)
        {
            if (item is not CoreObject obj || obj is IKeyFrame || pairs.ContainsKey(obj)) continue;

            int at = left.FindIndex(candidate => match(obj, candidate));
            if (at < 0) continue;

            pairs[obj] = left[at];
            left.RemoveAt(at);
        }
    }

    private static bool Contains(IList list, object obj)
    {
        foreach (object? item in list)
        {
            if (ReferenceEquals(item, obj)) return true;
        }

        return false;
    }

    // Held by owner as its own, as opposed to a reference to an object elsewhere.
    private static bool IsHeldBy(CoreObject value, CoreObject owner)
        => ReferenceEquals((value as IHierarchical)?.HierarchicalParent, owner);

    // Still in target, as what was found before can have left it since.
    private static bool IsUnder(CoreObject obj, Element target)
    {
        for (IHierarchical? current = obj as IHierarchical; current != null; current = current.HierarchicalParent)
        {
            if (ReferenceEquals(current, target)) return true;
        }

        return false;
    }

    // Objects of a kind that compares by its values are still told apart.
    private sealed class PairComparer : IEqualityComparer<(CoreObject, Element)>
    {
        public static readonly PairComparer Instance = new();

        public bool Equals((CoreObject, Element) x, (CoreObject, Element) y)
            => ReferenceEquals(x.Item1, y.Item1) && ReferenceEquals(x.Item2, y.Item2);

        public int GetHashCode((CoreObject, Element) obj)
            => HashCode.Combine(RuntimeHelpers.GetHashCode(obj.Item1), RuntimeHelpers.GetHashCode(obj.Item2));
    }
}
