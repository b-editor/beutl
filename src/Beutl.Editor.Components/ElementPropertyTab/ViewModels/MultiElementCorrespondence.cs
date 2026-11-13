using System.Collections;
using System.Runtime.CompilerServices;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.ElementPropertyTab.ViewModels;

// Which object of an element stands where each object of the copy does, the copy being what the element property tab
// shows for several elements. An object a property holds stands for the one the element's same property holds, when it
// is of the same kind; an item of a list stands for the item of the element's list that lining the two lists up by type
// pairs it with; a keyframe stands for the one at its time. The element's own objects are lined up by kind rather than
// by type, so that the drawable of one element stands for that of another even when one shows text and the other a
// shape: what both have in common, such as where they are drawn, can then be edited together.
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

    // Lines the lists up as their longest common sequence: of items of one type, and where byKind is set also of items
    // of one kind, which weigh less so that a pair of one type wins. Of the ways to line them up as well, the one that
    // pairs earlier items is taken, as the earlier of two items of one type stands for the earlier of the other list's.
    private static Dictionary<object, object> Pair(IReadOnlyList<object?> source, IList target, bool byKind)
    {
        var pairs = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        int n = source.Count;
        int m = target.Count;
        if (n == 0 || m == 0) return pairs;

        // best[i, j] is the weight of the best pairing of source[i..] with target[j..].
        var best = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
        {
            for (int j = m - 1; j >= 0; j--)
            {
                int weight = Weight(source[i], target[j], byKind);
                int paired = weight > 0 ? best[i + 1, j + 1] + weight : 0;
                best[i, j] = Math.Max(paired, Math.Max(best[i + 1, j], best[i, j + 1]));
            }
        }

        for (int i = 0, j = 0; i < n && j < m;)
        {
            int weight = Weight(source[i], target[j], byKind);
            if (weight > 0 && best[i, j] == best[i + 1, j + 1] + weight)
            {
                pairs[source[i]!] = target[j]!;
                i++;
                j++;
            }
            else if (best[i + 1, j] >= best[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return pairs;
    }

    private static int Weight(object? a, object? b, bool byKind)
    {
        if (a is not CoreObject x || b is not CoreObject y || a is IKeyFrame || b is IKeyFrame) return 0;
        if (x.GetType() == y.GetType()) return 2;
        return byKind && SameKind(x, y) ? 1 : 0;
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
