using Avalonia.Controls;
using Avalonia.Threading;
using FluentAvalonia.UI.Windowing;

namespace Beutl.HeadlessUITests;

internal static class HeadlessAppWindow
{
    private static readonly Action<bool> s_setDesignMode = typeof(Design)
        .GetProperty(nameof(Design.IsDesignMode))!
        .GetSetMethod(nonPublic: true)!
        .CreateDelegate<Action<bool>>();

    public static T Create<T>(Func<T> factory) where T : FAAppWindow
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!OperatingSystem.IsWindows())
            return factory();

        // FluentAvalonia 3.1.0 registers Headless's shared zero handle as an HWND (#2553).
        // Its design-mode path skips Win32 initialization. Scope it to construction so
        // opening, rendering and closing still run in the normal runtime mode.
        bool previous = Design.IsDesignMode;
        s_setDesignMode(true);
        try
        {
            return factory();
        }
        finally
        {
            s_setDesignMode(previous);
        }
    }
}
