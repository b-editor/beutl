using System.Runtime.InteropServices;

namespace Beutl.HeadlessUITests;

// The Objective-C runtime and CoreGraphics calls that the native scroll tests make on macOS.
internal static partial class MacNative
{
    public const string ObjC = "/usr/lib/libobjc.A.dylib";
    public const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint GetClass(string name);

    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint Selector(string name);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint receiver, nint selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint receiver, nint selector, nint argument);

    [LibraryImport(CoreGraphics)]
    public static partial nint CGEventCreateScrollWheelEvent2(nint source, uint units, uint wheelCount, int wheel1, int wheel2, int wheel3);

    [LibraryImport(CoreGraphics)]
    public static partial void CGEventSetTimestamp(nint cgEvent, ulong timestamp);

    [LibraryImport(CoreGraphics)]
    public static partial void CGEventSetIntegerValueField(nint cgEvent, uint field, long value);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static partial void CFRelease(nint value);
}
