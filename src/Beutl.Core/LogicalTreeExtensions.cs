namespace Beutl;

public static class LogicalTreeExtensions
{
    public static T FindRequiredLogicalParent<T>(this ILogicalElement self, bool includeSelf = false)
    {
        T? parent = FindLogicalParent<T>(self, includeSelf);
        if (parent == null) throw new LogicalTreeException("Cannot get parent.");

        return parent;
    }

    public static T? FindLogicalParent<T>(this ILogicalElement self, bool includeSelf = false)
    {
        try
        {
            ILogicalElement? obj = includeSelf ? self : self.LogicalParent;

            while (obj is not T)
            {
                if (obj is null)
                {
                    return default;
                }

                obj = obj.LogicalParent;
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

    public static ILogicalElement FindRequiredLogicalParent(this ILogicalElement self, Type type, bool includeSelf = false)
    {
        ILogicalElement? parent = FindLogicalParent(self, type, includeSelf);
        if (parent == null) throw new LogicalTreeException("Cannot get parent.");

        return parent;
    }

    public static ILogicalElement? FindLogicalParent(this ILogicalElement self, Type type, bool includeSelf = false)
    {
        try
        {
            ILogicalElement? obj = includeSelf ? self : self.LogicalParent;
            Type? objType = obj?.GetType();

            while (objType?.IsAssignableTo(type) != true)
            {
                if (obj is null)
                {
                    return default;
                }

                obj = obj.LogicalParent;
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

    public static ILogicalElement GetRoot(this ILogicalElement self)
    {
        ILogicalElement? current = self;

        while (true)
        {
            ILogicalElement? next;

            try
            {
                next = current.LogicalParent;
            }
            catch
            {
                return current;
            }

            if (next is null)
            {
                return current;
            }
            else
            {
                current = next;
            }
        }
    }

    public static IEnumerable<TResult> EnumerateAllChildren<TResult>(this ILogicalElement self)
    {
        foreach (ILogicalElement? item in self.LogicalChildren)
        {
            foreach (TResult? innerItem in EnumerateAllChildren<TResult>(item))
            {
                yield return innerItem;
            }

            if (item is TResult t) yield return t;
        }
    }
}
