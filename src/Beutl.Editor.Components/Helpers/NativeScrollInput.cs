using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;

namespace Beutl.Editor.Components.Helpers;

internal static partial class NativeScrollInput
{
    public static IDisposable? Attach(TopLevel root)
    {
        return OperatingSystem.IsWindows() && root.TryGetPlatformHandle()?.HandleDescriptor == "HWND"
            ? Windows.Attach(root) : null;
    }

    // Call synchronously from PointerWheelChanged: both APIs describe the native
    // event currently being dispatched, not the last device used by the application.
    public static bool UsesGestureAxes(PointerWheelEventArgs e)
    {
        if (e.Source is not Visual source
            || TopLevel.GetTopLevel(source)?.TryGetPlatformHandle() is not { } handle)
        {
            return false;
        }

        if (OperatingSystem.IsWindows() && handle.HandleDescriptor == "HWND")
        {
            return Windows.IsTouchpadScroll();
        }

        if (OperatingSystem.IsMacOS() && handle.HandleDescriptor is "NSWindow" or "NSView")
        {
            return MacOS.IsGestureScroll(e.Timestamp);
        }

        return false;
    }

    internal static bool IsMacGestureScroll(ulong timestamp, double nativeTimestamp, bool precise, nint phase, nint momentumPhase)
    {
        if ((ulong)(nativeTimestamp * 1000) != timestamp || !precise) return false;
        // High-resolution wheels can be precise without belonging to a touch gesture.
        return phase != 0 || momentumPhase != 0;
    }
}
