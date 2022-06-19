using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace BeUtl;
public static class FrameHelper
{
    public static void RemoveAllStack(this Frame frame, Func<object, bool> func)
    {
        for (int i = frame.BackStack.Count - 1; i >= 0; i--)
        {
            PageStackEntry item = frame.BackStack[i];
            if (func(item.Parameter))
            {
                frame.BackStack.RemoveAt(i);
            }
        }

        for (int i = frame.ForwardStack.Count - 1; i >= 0; i--)
        {
            PageStackEntry item = frame.ForwardStack[i];
            if (func(item.Parameter))
            {
                frame.ForwardStack.RemoveAt(i);
            }
        }
    }
}
