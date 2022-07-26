namespace BeUtl.Styling;

public static class LogicalTreeExtensions
{
    public static T FindRequiredStylingParent<T>(this IStylingElement self, bool includeSelf = false)
    {
        T? parent = FindStylingParent<T>(self, includeSelf);
        if (parent == null) throw new StylingTreeException("Cannot get parent.");

        return parent;
    }

    public static T? FindStylingParent<T>(this IStylingElement self, bool includeSelf = false)
    {
        try
        {
            IStylingElement? obj = includeSelf ? self : self.StylingParent;

            while (obj is not T)
            {
                if (obj is null)
                {
                    return default;
                }

                obj = obj.StylingParent;
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

    public static IStylingElement FindRequiredStylingParent(this IStylingElement self, Type type, bool includeSelf = false)
    {
        IStylingElement? parent = FindStylingParent(self, type, includeSelf);
        if (parent == null) throw new StylingTreeException("Cannot get parent.");

        return parent;
    }

    public static IStylingElement? FindStylingParent(this IStylingElement self, Type type, bool includeSelf = false)
    {
        try
        {
            IStylingElement? obj = includeSelf ? self : self.StylingParent;
            Type? objType = obj?.GetType();

            while (objType?.IsAssignableTo(type) != true)
            {
                if (obj is null)
                {
                    return default;
                }

                obj = obj.StylingParent;
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

    public static IStylingElement GetRoot(this IStylingElement self)
    {
        IStylingElement? current = self;

        while (true)
        {
            IStylingElement? next;

            try
            {
                next = current.StylingParent;
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

    public static IEnumerable<TResult> EnumerateAllChildren<TResult>(this IStylingElement self)
    {
        foreach (IStylingElement? item in self.StylingChildren)
        {
            foreach (TResult? innerItem in EnumerateAllChildren<TResult>(item))
            {
                yield return innerItem;
            }

            if (item is TResult t) yield return t;
        }
    }
}
