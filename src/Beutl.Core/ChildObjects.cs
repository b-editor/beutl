using System.Collections;
using Beutl.Serialization;

namespace Beutl;

// Names the objects a type holds, such as its IProperty members.
internal interface IChildObjectSource
{
    IEnumerable<object?> GetChildObjects();
}

// The one walk below an object that searches, Id lookups and duplication share. It never goes up to a parent.
internal static class ChildObjects
{
    public static IEnumerable<object> Of(object obj)
    {
        switch (obj)
        {
            case string:
                yield break;
            case CoreObject:
                // An object is walked through its properties below, never as a list.
                break;
            case IEnumerable items when ItemsMayHoldObjects(items):
                foreach (object? item in items)
                {
                    if (Unwrap(item) is { } value)
                        yield return value;
                }

                break;
        }

        if (obj is IChildObjectSource source)
        {
            foreach (object? child in source.GetChildObjects())
            {
                if (Unwrap(child) is { } value)
                    yield return value;
            }
        }

        if (obj is IHierarchical hierarchical)
        {
            foreach (IHierarchical child in hierarchical.HierarchicalChildren)
                yield return child;
        }
    }

    // Every object below root, root first and each once, in the order Find visits them.
    public static IEnumerable<object> Walk(object root)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance) { root };
        var pending = new Stack<IEnumerator<object>>();
        yield return root;
        pending.Push(Of(root).GetEnumerator());
        while (pending.TryPeek(out IEnumerator<object>? children))
        {
            if (!children.MoveNext())
            {
                pending.Pop().Dispose();
                continue;
            }

            object child = children.Current;
            if (child.GetType().IsValueType || visited.Add(child))
            {
                yield return child;
                pending.Push(Of(child).GetEnumerator());
            }
        }
    }

    // A value type holds no object to walk into, unless it is an Optional around one.
    public static bool MayHoldObjects(Type type) => !type.IsValueType || type.IsAssignableTo(typeof(IOptional));

    // A list of values, such as a float[], holds nothing to walk into however long it is.
    private static bool ItemsMayHoldObjects(IEnumerable items)
        => ArrayTypeHelpers.GetElementType(items.GetType()) is not { } type || MayHoldObjects(type);

    public static object? Unwrap(object? value)
        => value is IOptional optional ? optional.ToObject().GetValueOrDefault() : value;
}
