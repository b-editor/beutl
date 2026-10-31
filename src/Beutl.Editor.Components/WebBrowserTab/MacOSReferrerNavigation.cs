using System.Runtime.Versioning;

using Avalonia.Platform;

using static Beutl.Editor.Components.Helpers.ObjCRuntime;

namespace Beutl.Editor.Components.WebBrowserTab;

// NativeWebView.Navigate sends no request headers. WKWebView sends the referrer a loaded request names, keeps it
// across redirects, and a response that becomes a download is that same request.
[SupportedOSPlatform("macos")]
internal static class MacOSReferrerNavigation
{
    internal static bool TryNavigate(IAppleWKWebViewPlatformHandle handle, Uri uri, string referrer)
    {
        nint webView = handle.GetWKWebViewRetained();
        if (webView == 0) return false;
        try
        {
            nint url = Send(GetClass("NSURL"), Selector("URLWithString:"), AutoreleasedString(uri.AbsoluteUri));
            if (url == 0) return false;
            nint request = Send(GetClass("NSMutableURLRequest"), Selector("requestWithURL:"), url);
            if (request == 0) return false;
            SendVoid(request, Selector("setValue:forHTTPHeaderField:"), AutoreleasedString(referrer), AutoreleasedString("Referer"));
            Send(webView, Selector("loadRequest:"), request);
            return true;
        }
        finally
        {
            SendVoid(webView, Selector("release"));
        }
    }
}
