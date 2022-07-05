namespace BeUtl;

public static class LogicalTreeExtensions
{
    public static T FindRequiredLogicalParent<T>(this ILogicalElement self, bool includeSelf = false)
    {
        T? parent = FindLogicalParent<T>(self, includeSelf);
        if (parent == null) throw new ElementException("Cannot get parent.");

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
