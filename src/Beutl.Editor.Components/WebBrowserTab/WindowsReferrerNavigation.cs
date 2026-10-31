using System.Diagnostics;
using System.Runtime.InteropServices;

using Avalonia.Platform;
using static Beutl.Editor.Components.WebBrowserTab.WebView2Vtable;

namespace Beutl.Editor.Components.WebBrowserTab;

// NativeWebView.Navigate sends no request headers. WebView2 navigates with a request its environment creates,
// whose headers take the place of the ones the runtime adds.
internal static unsafe class WindowsReferrerNavigation
{
    internal static bool TryNavigate(IWindowsWebView2PlatformHandle handle, Uri uri, string referrer)
    {
        nint coreWebView = handle.CoreWebView2;
        if (coreWebView == 0) return false;
        nint webView = 0, environment = 0, requestFactory = 0, request = 0;
        try
        {
            var webViewIid = new Guid("9E8F0CF8-E670-4B5E-B2BC-73E061E3184C"); // ICoreWebView2_2
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(coreWebView, in webViewIid, out webView));
            // ICoreWebView2_2::get_Environment
            environment = GetObject(webView, 67);
            var environmentIid = new Guid("41F3632B-5EF4-404F-AD82-2D606C5A9A21"); // ICoreWebView2Environment2
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(environment, in environmentIid, out requestFactory));
            fixed (char* address = uri.AbsoluteUri)
            fixed (char* method = "GET")
            fixed (char* headers = $"Referer: {referrer}")
            {
                // ICoreWebView2Environment2::CreateWebResourceRequest, with no POST data
                nint created;
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, char*, char*, nint, char*, nint*, int>)Method(requestFactory, 8))(
                    requestFactory, address, method, 0, headers, &created));
                request = created;
            }
            // ICoreWebView2_2::NavigateWithWebResourceRequest
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, int>)Method(webView, 63))(webView, request));
            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceError("WebView2 could not navigate with the opener's referrer: {0}", ex);
            return false;
        }
        finally
        {
            if (request != 0) Marshal.Release(request);
            if (requestFactory != 0) Marshal.Release(requestFactory);
            if (environment != 0) Marshal.Release(environment);
            if (webView != 0) Marshal.Release(webView);
            Marshal.Release(coreWebView);
        }
    }
}
