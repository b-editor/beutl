using System.Runtime.InteropServices;

namespace Beutl.Editor.Components.WebBrowserTab;

internal static partial class BrowserWebViewEnvironment
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    internal static string? ReadBundleVersion(string bundlePath)
    {
        nint path = CreateString(bundlePath);
        nint bundle = Send(Send(GetClass("NSBundle"), Selector("alloc")), Selector("initWithPath:"), path);
        Send(path, Selector("release"));
        if (bundle == 0) return null;

        nint key = CreateString("CFBundleShortVersionString");
        nint value = Send(bundle, Selector("objectForInfoDictionaryKey:"), key);
        // A string describes itself, and a value of any other type answers description instead of raising.
        string? version = value == 0
            ? null
            : Marshal.PtrToStringUTF8(Send(Send(value, Selector("description")), Selector("UTF8String")));
        Send(key, Selector("release"));
        Send(bundle, Selector("release"));
        return version;
    }

    private static nint CreateString(string value) =>
        InitString(Send(GetClass("NSString"), Selector("alloc")), Selector("initWithUTF8String:"), value);

    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)] private static partial nint GetClass(string name);
    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)] private static partial nint Selector(string name);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] private static partial nint Send(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] private static partial nint Send(nint receiver, nint selector, nint argument);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)] private static partial nint InitString(nint receiver, nint selector, string value);
}
