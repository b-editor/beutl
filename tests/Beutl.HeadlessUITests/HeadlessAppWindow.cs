using Avalonia.Controls;
using Avalonia.Threading;
using Beutl.Controls;
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
        T CreateWindow()
        {
            T window = factory();
            if (ControlConfiguration.GetConfiguration(window) == null)
                ControlConfiguration.SetConfiguration(window, TestShell.Configuration);
            if (ControlDecoders.GetDecoders(window) == null)
                ControlDecoders.SetDecoders(window, TestShell.Decoders);
            if (ControlFonts.GetFonts(window) == null)
                ControlFonts.SetFonts(window, TestShell.FontManager);
            if (ControlNotifications.GetNotifications(window) == null)
                ControlNotifications.SetNotifications(window, TestShell.Notifications);
            return window;
        }

        if (!OperatingSystem.IsWindows())
            return CreateWindow();

        // FluentAvalonia 3.1.0 registers Headless's shared zero handle as an HWND (#2553).
        // Its design-mode path skips Win32 initialization. Scope it to construction so
        // opening, rendering and closing still run in the normal runtime mode.
        bool previous = Design.IsDesignMode;
        s_setDesignMode(true);
        try
        {
            return CreateWindow();
        }
        finally
        {
            s_setDesignMode(previous);
        }
    }
}
