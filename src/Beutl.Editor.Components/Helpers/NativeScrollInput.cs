using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Beutl.Editor.Components.Helpers;

internal static partial class NativeScrollInput
{
    private static Pointer? s_gesturePointer;

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

    // Delivers a pinch as Avalonia delivers a native trackpad magnification on macOS, so the views
    // that zoom on a Mac pinch zoom by the same handlers. The element under the pointer, or its first
    // enabled ancestor, receives the event; pointer capture is not consulted.
    internal static bool RaiseMagnify(TopLevel root, Point position, double magnification, KeyModifiers modifiers, ulong timestamp)
    {
        IInputElement? source = root.InputHitTest(position, enabledElementsOnly: false);
        while (source is { IsEffectivelyEnabled: false })
        {
            source = (source as Visual)?.GetVisualParent() as IInputElement;
        }

        if (source == null) return false;
        s_gesturePointer ??= new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var e = new PointerDeltaEventArgs(InputElement.PointerTouchPadGestureMagnifyEvent, source, s_gesturePointer,
            root, position, timestamp, PointerPointProperties.None, modifiers, new Vector(magnification, magnification));
        source.RaiseEvent(e);
        return e.Handled;
    }

    // A magnification is the relative scale change (macOS's, and RaiseMagnify's factor - 1), not a wheel delta.
    internal static bool TryGetMagnifyFactor(PointerDeltaEventArgs e, out double factor)
    {
        factor = 1 + e.Delta.X;
        return double.IsFinite(factor) && factor > 0;
    }

    internal static bool IsMacGestureScroll(ulong timestamp, double nativeTimestamp, bool precise, nint phase, nint momentumPhase)
    {
        if ((ulong)(nativeTimestamp * 1000) != timestamp || !precise) return false;
        // High-resolution wheels can be precise without belonging to a touch gesture.
        return phase != 0 || momentumPhase != 0;
    }
}
