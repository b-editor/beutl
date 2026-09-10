using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace Beutl;

public static class FAFrameHelper
{
    public static void RemoveAllStack(this FAFrame frame, Func<object, bool> func)
    {
        for (int i = frame.BackStack.Count - 1; i >= 0; i--)
        {
            FAPageStackEntry item = frame.BackStack[i];
            if (func(item.Parameter))
            {
                frame.BackStack.RemoveAt(i);
            }
        }

        for (int i = frame.ForwardStack.Count - 1; i >= 0; i--)
        {
            FAPageStackEntry item = frame.ForwardStack[i];
            if (func(item.Parameter))
            {
                frame.ForwardStack.RemoveAt(i);
            }
        }
    }

    public static T? FindParameter<T>(this FAFrame frame, Func<T, bool> func)
    {
        for (int i = 0; i < frame.BackStack.Count; i++)
        {
            FAPageStackEntry item = frame.BackStack[i];
            if (item.Parameter is T typed && func(typed))
            {
                return typed;
            }
        }

        for (int i = 0; i < frame.ForwardStack.Count; i++)
        {
            FAPageStackEntry item = frame.ForwardStack[i];
            if (item.Parameter is T typed && func(typed))
            {
                return typed;
            }
        }

        return default;
    }
}
