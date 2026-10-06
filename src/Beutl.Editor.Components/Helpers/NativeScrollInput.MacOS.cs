using System.Runtime.InteropServices;

namespace Beutl.Editor.Components.Helpers;

internal static partial class NativeScrollInput
{
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
                && IsMacGestureScroll(timestamp, SendDouble(currentEvent, s_timestamp),
                    SendByte(currentEvent, s_hasPreciseScrollingDeltas) != 0,
                    Send(currentEvent, s_phase), Send(currentEvent, s_momentumPhase));
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
