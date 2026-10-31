using System.Runtime.InteropServices;

namespace Beutl.Editor.Components.Helpers;

// Objective-C runtime plumbing shared by the native macOS integrations.
internal static partial class ObjCRuntime
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    // The caller owns the string and releases it.
    internal static nint NewString(string value) =>
        SendString(Send(GetClass("NSString"), Selector("alloc")), Selector("initWithUTF8String:"), value);

    // The current autorelease pool owns the string.
    internal static nint AutoreleasedString(string value) =>
        SendString(GetClass("NSString"), Selector("stringWithUTF8String:"), value);

    internal static string? ToManagedString(nint value) =>
        value == 0 ? null : Marshal.PtrToStringUTF8(Send(value, Selector("UTF8String")));

    [LibraryImport(LibObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GetClass(string name);

    [LibraryImport(LibObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint Selector(string name);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint Send(nint receiver, nint selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint Send(nint receiver, nint selector, nint argument);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint Send(nint receiver, nint selector, nint first, nint second);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint Send(nint receiver, nint selector, nint first, nint second, nint third);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    internal static partial void SendVoid(nint receiver, nint selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    internal static partial void SendVoid(nint receiver, nint selector, nint argument);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    internal static partial void SendVoid(nint receiver, nint selector, nint first, nint second);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool SendBool(nint receiver, nint selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool SendBool(nint receiver, nint selector, nint argument);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    internal static partial byte SendByte(nint receiver, nint selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    internal static partial double SendDouble(nint receiver, nint selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint SendString(nint receiver, nint selector, string value);
}
