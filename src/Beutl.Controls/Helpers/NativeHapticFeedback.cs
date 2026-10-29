using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace Beutl.Controls;

internal enum HapticFeedbackKind
{
    Alignment = 1
}

internal static partial class NativeHapticFeedback
{
    // The host can substitute the output without changing gesture handling.
    internal static Action<Visual, HapticFeedbackKind, ulong> Performer { get; set; } = PerformNative;

    public static void PerformAlignment(Visual source, ulong timestamp)
        => Performer(source, HapticFeedbackKind.Alignment, timestamp);

    // Called synchronously by the drag handler. A native window also keeps headless
    // previews from loading AppKit or producing feedback on the host's trackpad.
    private static void PerformNative(Visual source, HapticFeedbackKind kind, ulong timestamp)
    {
        if (OperatingSystem.IsMacOS()
            && TopLevel.GetTopLevel(source)?.TryGetPlatformHandle()?.HandleDescriptor is "NSWindow" or "NSView")
        {
            MacOS.PerformFeedback(kind, timestamp);
        }
    }

    [ExcludeFromCodeCoverage(Justification = "AppKit interop is covered by macOS-only tests; CI collects coverage on Linux.")]
    internal static partial class MacOS
    {
        private const string ObjC = "/usr/lib/libobjc.A.dylib";
        private static readonly nint s_applicationClass = GetClass("NSApplication");
        private static readonly nint s_sharedApplication = Selector("sharedApplication");
        private static readonly nint s_currentEvent = Selector("currentEvent");
        private static readonly nint s_type = Selector("type");
        private static readonly nint s_subtype = Selector("subtype");
        private static readonly nint s_timestamp = Selector("timestamp");
        private static readonly nint s_managerClass = GetClass("NSHapticFeedbackManager");
        private static readonly nint s_defaultPerformer = Selector("defaultPerformer");
        private static readonly nint s_performFeedback = Selector("performFeedbackPattern:performanceTime:");

        // Do not load Objective-C on other platforms.
        static MacOS() { }

        public static void PerformFeedback(HapticFeedbackKind kind, ulong timestamp)
        {
            nint application = Send(s_applicationClass, s_sharedApplication);
            if (!IsTrackpadMouseEvent(Send(application, s_currentEvent), timestamp)) return;

            // AppKit chooses the current device and honors the user's accessibility
            // and feedback preferences. The performer must not be cached.
            nint performer = Send(s_managerClass, s_defaultPerformer);
            const nuint Now = 1; // NSHapticFeedbackPerformanceTimeNow (NSUInteger)
            Perform(performer, s_performFeedback, (nint)kind, Now);
        }

        internal static bool IsTrackpadMouseEvent(nint currentEvent, ulong timestamp)
        {
            if (currentEvent == 0) return false;

            nint type = Send(currentEvent, s_type);
            // Left/right/move/drag and other-button mouse events. Scrolling and pen
            // events must not be mistaken for trackpad movement (NSEventSubtypeTouch = 3).
            // Avalonia.Native converts the native event's seconds to milliseconds.
            // NSApplication.currentEvent can still refer to an earlier dispatch.
            return (type is >= 1 and <= 7 or >= 25 and <= 27)
                && SendShort(currentEvent, s_subtype) == 3
                && (ulong)(SendDouble(currentEvent, s_timestamp) * 1000) == timestamp;
        }

        [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint GetClass(string name);

        [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint Selector(string name);

        [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
        private static partial nint Send(nint receiver, nint selector);

        [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
        private static partial short SendShort(nint receiver, nint selector);

        [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
        private static partial double SendDouble(nint receiver, nint selector);

        [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
        private static partial void Perform(nint receiver, nint selector, nint pattern, nuint performanceTime);
    }
}
