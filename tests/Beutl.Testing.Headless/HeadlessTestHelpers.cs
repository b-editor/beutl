using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Beutl.Testing.Headless;

public static class HeadlessTestHelpers
{
    public static void Settle(int rounds = 2)
    {
        for (int i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    public static void Render(int ticks = 1)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(ticks);
        Settle();
    }

    public static T? FindDescendant<T>(Visual root)
        where T : Visual
    {
        foreach (Visual child in root.GetVisualChildren())
        {
            if (child is T match)
            {
                return match;
            }

            T? found = FindDescendant<T>(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
