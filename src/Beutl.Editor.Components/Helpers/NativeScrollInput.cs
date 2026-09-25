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

    internal static partial class Windows
    {
        private const uint Touchpad = 0x10; // IMDT_TOUCHPAD
        private static readonly Func<bool, bool>? s_registerTouchpadThread = LoadTouchpadRegistration();
        private static ThreadRegistration? s_registration;

        static Windows() { }

        internal static bool IsTouchpadScroll() => IsTouchpadScroll(GetCurrentDeviceType);

        internal static bool IsTouchpadScroll(Func<uint?> getCurrentDeviceType) => getCurrentDeviceType() == Touchpad;

        internal static IDisposable? Attach(TopLevel root)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (s_registration == null)
            {
                var registration = new ThreadRegistration();
                if (!registration.Start(root)) return null;
                s_registration = registration;
            }

            return s_registration.Acquire(root);
        }

        internal static IDisposable? RegisterInput(Func<bool, bool>? registerThread, Action installHooks, Action removeHooks)
        {
            if (registerThread == null) return null;
            bool registered = false;
            try
            {
                installHooks();
                registered = registerThread(true);
                if (!registered) return null;
                return System.Reactive.Disposables.Disposable.Create(() =>
                {
                    try { registerThread(false); }
                    finally { removeHooks(); }
                });
            }
            finally
            {
                if (!registered) removeHooks();
            }
        }

        private sealed class ThreadRegistration
        {
            private readonly HashSet<TopLevel> _roots = [];
            private IDisposable? _visibilitySubscription;
            private IDisposable? _inputRegistration;
            private int _owners;

            public bool Start(TopLevel root)
            {
                _inputRegistration = RegisterInput(s_registerTouchpadThread, () =>
                {
                    // Thread registration affects other windows and native popups too.
                    // Hook them before enabling delivery and track subsequently shown top levels.
                    _visibilitySubscription = Visual.IsVisibleProperty.Changed.AddClassHandler<TopLevel>((topLevel, _) =>
                    {
                        if (topLevel.IsVisible) Track(topLevel);
                    });
                    Track(root);
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                        foreach (Window window in lifetime.Windows) Track(window);
                }, RemoveHooks);
                return _inputRegistration != null;
            }

            public IDisposable Acquire(TopLevel root)
            {
                Track(root);
                _owners++;
                return System.Reactive.Disposables.Disposable.Create(() =>
                {
                    Dispatcher.UIThread.VerifyAccess();
                    if (--_owners != 0) return;
                    s_registration = null;
                    _inputRegistration?.Dispose();
                });
            }

            private void Track(TopLevel root)
            {
                if (root.TryGetPlatformHandle()?.HandleDescriptor != "HWND" || !_roots.Add(root)) return;
                Win32Properties.AddWndProcHookCallback(root, ForwardTouchpadMessage);
                root.Closed += OnClosed;
            }

            private void OnClosed(object? sender, EventArgs e)
            {
                if (sender is TopLevel root) Untrack(root);
            }

            private void Untrack(TopLevel root)
            {
                if (!_roots.Remove(root)) return;
                root.Closed -= OnClosed;
                Win32Properties.RemoveWndProcHookCallback(root, ForwardTouchpadMessage);
            }

            private void RemoveHooks()
            {
                _visibilitySubscription?.Dispose();
                foreach (TopLevel root in _roots.ToArray()) Untrack(root);
            }
        }

        private static nint ForwardTouchpadMessage(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled)
            => ForwardTouchpadMessage(hwnd, message, wParam, lParam, ref handled, GetPointerDeviceType, DefWindowProc);

        internal static nint ForwardTouchpadMessage(nint hwnd, uint message, nint wParam, nint lParam,
            ref bool handled, Func<uint, uint?> getPointerType, Func<nint, uint, nint, nint, nint> defaultProc)
        {
            if (!handled && IsPointerMessage(message)
                && getPointerType((uint)((nuint)wParam & 0xffff)) == 5) // PT_TOUCHPAD
            {
                // Avalonia 12 treats unknown pointer types as mouse button/move input.
                // Let Windows recognize the gesture and generate its normal wheel messages instead.
                handled = true;
                return defaultProc(hwnd, message, wParam, lParam);
            }
            return 0;
        }

        private static uint? GetPointerDeviceType(uint id) => GetPointerType(id, out uint type) ? type : null;

        private static bool IsPointerMessage(uint message) => message is
            0x0241 or 0x0242 or 0x0243 or 0x0245 or 0x0246 or 0x0247 or
            0x0249 or 0x024a or 0x024b or 0x024c or 0x024e or 0x024f or 0x0251 or 0x0252 or 0x0253;

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

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetPointerType(uint pointerId, out uint pointerType);

        [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
        private static partial nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);

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
