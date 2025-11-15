namespace Beutl;

public static class HierarchicalExtensions
{
    public static T FindRequiredHierarchicalParent<T>(this IHierarchical self, bool includeSelf = false)
    {
        T? parent = self.FindHierarchicalParent<T>(includeSelf);
        if (parent == null) throw new HierarchyException("Cannot get parent.");

        return parent;
    }

    public static T? FindHierarchicalParent<T>(this IHierarchical self, bool includeSelf = false)
    {
        try
        {
            IHierarchical? obj = includeSelf ? self : self.HierarchicalParent;

            while (obj is not T)
            {
                if (obj is null)
                {
                    return default;
                }

                obj = obj.HierarchicalParent;
            }

            if (obj is T result)
            {
                return result;
            }
            else
            {
                return default;
            }
        }
        catch
        {
            return default;
        }
    }

    public static IHierarchical FindRequiredHierarchicalParent(this IHierarchical self, Type type, bool includeSelf = false)
    {
        IHierarchical? parent = self.FindHierarchicalParent(type, includeSelf);
        if (parent == null) throw new HierarchyException("Cannot get parent.");

        return parent;
    }

    public static IHierarchical? FindHierarchicalParent(this IHierarchical self, Type type, bool includeSelf = false)
    {
        try
        {
            IHierarchical? obj = includeSelf ? self : self.HierarchicalParent;
            Type? objType = obj?.GetType();

            while (objType?.IsAssignableTo(type) != true)
            {
                if (obj is null)
                {
                    return default;
                }

                obj = obj.HierarchicalParent;
                objType = obj?.GetType();
            }

            if (obj != null && objType?.IsAssignableTo(type) == true)
            {
                return obj;
            }
            else
            {
                return default;
            }
        }
        catch
        {
            return default;
        }
    }

    public static IHierarchicalRoot? FindHierarchicalRoot(this IHierarchical self)
    {
        while (self != null)
        {
            if (self is IHierarchicalRoot root)
            {
                return root;
            }

            self = self.HierarchicalParent!;
        }

        return null;
    }

    public static IEnumerable<TResult> EnumerateAllChildren<TResult>(this IHierarchical self)
    {
        foreach (IHierarchical? item in self.HierarchicalChildren)
        {
            foreach (TResult? innerItem in EnumerateAllChildren<TResult>(item))
            {
                yield return innerItem;
            }

            if (item is TResult t) yield return t;
        }
    }

    public static IEnumerable<TResult> EnumerateAncestors<TResult>(this IHierarchical self)
    {
        IHierarchical? parent = self;

        while (parent != null)
        {
            if (parent is TResult t) yield return t;
            parent = parent.HierarchicalParent;
        }
    }
}
