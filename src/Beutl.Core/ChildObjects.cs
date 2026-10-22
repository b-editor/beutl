using System.Collections;
using Beutl.Serialization;

namespace Beutl;

// Names the objects a type holds outside its registered CoreProperties, such as its IProperty members.
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
            case CoreObject coreObject:
                foreach (CoreProperty property in PropertyRegistry.GetRegistered(coreObject.GetType()))
                {
                    if (MayHoldObjects(property.PropertyType)
                        && Unwrap(coreObject.GetValue(property)) is { } value)
                    {
                        yield return value;
                    }
                }

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

    // A value type holds no object to walk into, unless it is an Optional around one.
    public static bool MayHoldObjects(Type type) => !type.IsValueType || type.IsAssignableTo(typeof(IOptional));

    // A list of values, such as a float[], holds nothing to walk into however long it is.
    private static bool ItemsMayHoldObjects(IEnumerable items)
        => ArrayTypeHelpers.GetElementType(items.GetType()) is not { } type || MayHoldObjects(type);

    public static object? Unwrap(object? value)
        => value is IOptional optional ? optional.ToObject().GetValueOrDefault() : value;
}
