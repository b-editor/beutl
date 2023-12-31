using System.Collections;

namespace Beutl.Services;

public class ObjectSearcher
{
    private readonly HashSet<object> _hashSet = [];
    private readonly Stack<object>? _stack;
    private readonly Func<Stack<object>, object, bool> _predicate;
    private readonly object _obj;

    public ObjectSearcher(object obj, Predicate<object> predicate)
    {
        _predicate = (_, o) => predicate(o);
        _obj = obj;
    }

    public ObjectSearcher(object obj, Func<Stack<object>, object, bool> predicate)
    {
        _stack = new();
        _predicate = predicate;
        _obj = obj;
    }

    public void Reset()
    {
        _hashSet.Clear();
        _stack?.Clear();
    }

    public object? Search()
    {
        return SearchRecursive(_obj);
    }

    public IReadOnlyList<object> SearchAll()
    {
        var list = new List<object>();
        SearchAllRecursive(_obj, list);
        return list;
    }

    private object? SearchRecursive(object obj)
    {
        if (!_hashSet.Add(obj))
            return null;

        if (_predicate(_stack!, obj))
            return obj;

        try
        {
            _stack?.Push(obj);

            switch (obj)
            {
                case CoreObject coreObject:
                    foreach (CoreProperty? item in PropertyRegistry.GetRegistered(coreObject.GetType())
                        .Where(x => !x.PropertyType.IsValueType && x != Hierarchical.HierarchicalParentProperty))
                    {
                        object? value = coreObject.GetValue(item);
                        if (value != null
                            && SearchRecursive(value) is { } result)
                        {
                            return result;
                        }
                    }
                    break;

                case IEnumerable enm:
                    {
                        foreach (object? item in enm)
                        {
                            if (item != null
                                && SearchRecursive(item) is { } result)
                            {
                                return result;
                            }
                        }
                    }
                    break;

                case IAbstractProperty property:
                    {
                        if (!property.PropertyType.IsValueType
                            && property.GetValue() is { } value
                            && SearchRecursive(value) is { } result1)
                        {
                            return result1;
                        }

                        if (property is IAbstractAnimatableProperty { Animation: { } animation }
                            && SearchRecursive(animation) is { } result2)
                        {
                            return result2;
                        }
                    }
                    break;
            }

            return null;
        }
        finally
        {
            _stack?.Pop();
        }
    }

    private void SearchAllRecursive(object obj, List<object> list)
    {
        if (!_hashSet.Add(obj))
            return;

        if (_predicate(_stack!, obj))
            list.Add(obj);

        try
        {
            _stack?.Push(obj);

            switch (obj)
            {
                case CoreObject coreObject:
                    foreach (object? item in PropertyRegistry.GetRegistered(coreObject.GetType())
                        .Where(x => !x.PropertyType.IsValueType && x != Hierarchical.HierarchicalParentProperty)
                        .Select(coreObject.GetValue)
                        .Where(x => x != null))
                    {
                        SearchAllRecursive(item!, list);
                    }
                    break;

                case IEnumerable enm:
                    {
                        foreach (object? item in enm)
                        {
                            if (item != null)
                                SearchAllRecursive(item, list);
                        }
                    }
                    break;

                case IAbstractProperty property:
                    {
                        if (!property.PropertyType.IsValueType
                            && property.GetValue() is { } value)
                        {
                            SearchAllRecursive(value, list);
                        }

                        if (property is IAbstractAnimatableProperty { Animation: { } animation })
                        {
                            SearchAllRecursive(animation, list);
                        }
                    }
                    break;
            }
        }
        finally
        {
            _stack?.Pop();
        }
    }
}
