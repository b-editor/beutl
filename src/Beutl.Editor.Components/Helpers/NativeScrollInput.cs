using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Beutl.Editor.Components.Helpers;

internal static partial class NativeScrollInput
{
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

    internal static partial class Windows
    {
        private const uint Touchpad = 0x10; // IMDT_TOUCHPAD
        private static readonly Func<bool, bool>? s_registerTouchpadThread = LoadTouchpadRegistration();

        static Windows() { }

        internal static bool IsTouchpadScroll() => IsTouchpadScroll(s_registerTouchpadThread, GetCurrentDeviceType);

        internal static bool IsTouchpadScroll(Func<bool, bool>? registerThread, Func<uint?> getDeviceType)
        {
            // Without opting in, Windows can report touchpad-originated mouse messages as IMDT_MOUSE.
            // Register only while querying: do not change the window's normal WM_POINTER delivery.
            bool registered = registerThread?.Invoke(true) == true;
            try
            {
                return getDeviceType() == Touchpad;
            }
            finally
            {
                if (registered) registerThread!(false);
            }
        }

        private static uint? GetCurrentDeviceType()
        {
            return GetCurrentInputMessageSource(out InputMessageSource input) ? input.DeviceType : null;
        }

        private static Func<bool, bool>? LoadTouchpadRegistration()
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return null;
            nint user32 = GetModuleHandle("user32.dll");
            if (user32 == 0) return null;

            // Windows 11 exposes this optional API by ordinal on some versions.
            // https://learn.microsoft.com/windows/win32/input-precisiontouchpad/registertouchpadcapable
            if (!NativeLibrary.TryGetExport(user32, "RegisterTouchpadCapableThread", out nint address))
            {
                address = GetProcAddress(user32, 2688);
            }

            if (address == 0) return null;
            var register = Marshal.GetDelegateForFunctionPointer<RegisterTouchpadThread>(address);
            return register.Invoke;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private delegate bool RegisterTouchpadThread([MarshalAs(UnmanagedType.Bool)] bool enable);

        [StructLayout(LayoutKind.Sequential)]
        private struct InputMessageSource
        {
            public uint DeviceType;
            public uint OriginId;
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetCurrentInputMessageSource(out InputMessageSource source);

        [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
        private static partial nint GetModuleHandle(string module);

        [LibraryImport("kernel32.dll")]
        private static partial nint GetProcAddress(nint module, nint ordinal);
    }

    internal static partial class MacOS
    {
        private const string ObjC = "/usr/lib/libobjc.A.dylib";
        private static readonly nint s_applicationClass = GetClass("NSApplication");
        private static readonly nint s_sharedApplication = Selector("sharedApplication");
        private static readonly nint s_currentEvent = Selector("currentEvent");
        private static readonly nint s_type = Selector("type");
        private static readonly nint s_timestamp = Selector("timestamp");
        private static readonly nint s_hasPreciseScrollingDeltas = Selector("hasPreciseScrollingDeltas");
        private static readonly nint s_phase = Selector("phase");
        private static readonly nint s_momentumPhase = Selector("momentumPhase");

        // Do not load Objective-C on other platforms.
        static MacOS() { }

        internal static bool IsGestureScroll(ulong timestamp)
        {
            nint application = Send(s_applicationClass, s_sharedApplication);
            nint currentEvent = Send(application, s_currentEvent);
            return IsGestureScrollEvent(currentEvent, timestamp);
        }

        internal static bool IsGestureScrollEvent(nint currentEvent, ulong timestamp)
        {
            const int ScrollWheel = 22; // NSEventTypeScrollWheel

            // currentEvent may still refer to an earlier event after dispatch.
            // Avalonia.Native also converts NSEvent.timestamp from seconds to milliseconds.
            return currentEvent != 0
                && Send(currentEvent, s_type) == ScrollWheel
                && (ulong)(SendDouble(currentEvent, s_timestamp) * 1000) == timestamp
                && SendByte(currentEvent, s_hasPreciseScrollingDeltas) != 0
                // High-resolution mouse wheels can also provide precise deltas.
                // Preserve axes only for a gesture or its momentum, not precision alone.
                && (Send(currentEvent, s_phase) != 0 || Send(currentEvent, s_momentumPhase) != 0);
        }

        [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint GetClass(string name);

        [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint Selector(string name);

        [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
        private static partial nint Send(nint receiver, nint selector);

        [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
        private static partial double SendDouble(nint receiver, nint selector);

        [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
        private static partial byte SendByte(nint receiver, nint selector);
    }
}
