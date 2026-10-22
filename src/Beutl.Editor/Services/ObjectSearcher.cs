namespace Beutl.Editor.Services;

// Searches below an object along ChildObjects, which reaches IProperty members, keyframes and node-port values.
public class ObjectSearcher
{
    private readonly HashSet<object> _hashSet = new(ReferenceEqualityComparer.Instance);
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

    public object? Search()
    {
        return ChildObjects.Unwrap(_obj) is { } root ? SearchRecursive(root) : null;
    }

    public IReadOnlyList<object> SearchAll()
    {
        var list = new List<object>();
        if (ChildObjects.Unwrap(_obj) is { } root)
            SearchAllRecursive(root, list);
        return list;
    }

    private object? SearchRecursive(object obj)
    {
        if (!obj.GetType().IsValueType && !_hashSet.Add(obj))
            return null;

        if (_predicate(_stack!, obj))
            return obj;

        try
        {
            _stack?.Push(obj);
            foreach (object child in ChildObjects.Of(obj))
            {
                if (SearchRecursive(child) is { } result)
                    return result;
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
        if (!obj.GetType().IsValueType && !_hashSet.Add(obj))
            return;

        if (_predicate(_stack!, obj))
            list.Add(obj);

        try
        {
            _stack?.Push(obj);
            foreach (object child in ChildObjects.Of(obj))
                SearchAllRecursive(child, list);
        }
        finally
        {
            _stack?.Pop();
        }
    }
}
