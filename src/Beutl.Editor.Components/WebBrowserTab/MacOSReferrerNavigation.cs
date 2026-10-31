using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Avalonia.Platform;

namespace Beutl.Editor.Components.WebBrowserTab;

// NativeWebView.Navigate sends no request headers. WKWebView sends the referrer a loaded request names, keeps it
// across redirects, and a response that becomes a download is that same request.
[SupportedOSPlatform("macos")]
internal static partial class MacOSReferrerNavigation
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    internal static bool TryNavigate(IAppleWKWebViewPlatformHandle handle, Uri uri, string referrer)
    {
        nint webView = handle.GetWKWebViewRetained();
        if (webView == 0) return false;
        try
        {
            nint url = SendObject(objc_getClass("NSURL"), sel_registerName("URLWithString:"), CreateString(uri.AbsoluteUri));
            if (url == 0) return false;
            nint request = SendObject(objc_getClass("NSMutableURLRequest"), sel_registerName("requestWithURL:"), url);
            if (request == 0) return false;
            SendObjects(request, sel_registerName("setValue:forHTTPHeaderField:"), CreateString(referrer), CreateString("Referer"));
            SendObject(webView, sel_registerName("loadRequest:"), request);
            return true;
        }
        finally
        {
            SendVoid(webView, sel_registerName("release"));
        }
    }

    private static nint CreateString(string value) =>
        SendString(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), value);

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_getClass(string name);

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendVoid(nint receiver, nint selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint SendString(nint receiver, nint selector, string value);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendObject(nint receiver, nint selector, nint value);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendObjects(nint receiver, nint selector, nint first, nint second);
}
