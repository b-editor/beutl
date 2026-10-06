using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace Beutl;

public static class FAFrameHelper
{
    public static void RemoveAllStack(this FAFrame frame, Func<object, bool> func)
    {
        RemoveAll(frame.BackStack, func);
        RemoveAll(frame.ForwardStack, func);
    }

    public static T? FindParameter<T>(this FAFrame frame, Func<T, bool> func)
    {
        if (TryFindParameter(frame.BackStack, func, out T? found)
            || TryFindParameter(frame.ForwardStack, func, out found))
        {
            return found;
        }

        return default;
    }

    private static void RemoveAll(IList<FAPageStackEntry> stack, Func<object, bool> func)
    {
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            FAPageStackEntry item = stack[i];
            if (func(item.Parameter))
            {
                stack.RemoveAt(i);
            }
        }
    }

    private static bool TryFindParameter<T>(IList<FAPageStackEntry> stack, Func<T, bool> func, out T? found)
    {
        for (int i = 0; i < stack.Count; i++)
        {
            FAPageStackEntry item = stack[i];
            if (item.Parameter is T typed && func(typed))
            {
                found = typed;
                return true;
            }
        }

        found = default;
        return false;
    }
}
