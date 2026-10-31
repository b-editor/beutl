using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text.Json.Nodes;

using Avalonia.Platform;
using static Beutl.Editor.Components.WebBrowserTab.WebView2Vtable;

namespace Beutl.Editor.Components.WebBrowserTab;

// NativeWebView.Navigate sends no request headers, and WebView2 drops a Referer that the request for
// NavigateWithWebResourceRequest names. The DevTools protocol's Page.navigate gives the navigation its referrer,
// even while the DevTools window is disabled.
internal static unsafe partial class WindowsReferrerNavigation
{
    internal static bool TryNavigate(IWindowsWebView2PlatformHandle handle, Uri uri, string referrer, Action fallback)
    {
        nint webView = handle.CoreWebView2;
        if (webView == 0) return false;
        void* completed = null;
        try
        {
            string parameters = new JsonObject { ["url"] = uri.AbsoluteUri, ["referrer"] = referrer }.ToJsonString();
            completed = ComInterfaceMarshaller<IDevToolsMethodCompletedHandler>.ConvertToUnmanaged(new NavigateCompletedHandler(fallback));
            fixed (char* method = "Page.navigate")
            fixed (char* json = parameters)
            {
                // ICoreWebView2::CallDevToolsProtocolMethod
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, char*, char*, void*, int>)Method(webView, 36))(
                    webView, method, json, completed));
            }
            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceError("WebView2 could not navigate with the opener's referrer: {0}", ex);
            return false;
        }
        finally
        {
            if (completed != null) ComInterfaceMarshaller<IDevToolsMethodCompletedHandler>.Free(completed);
            Marshal.Release(webView);
        }
    }

    [GeneratedComClass]
    private sealed partial class NavigateCompletedHandler(Action fallback) : IDevToolsMethodCompletedHandler
    {
        // A failed call started no navigation. A navigation that starts and then fails reports success here.
        public void Invoke(int errorCode, nint returnObjectAsJson)
        {
            if (errorCode >= 0) return;
            Trace.TraceError("WebView2 refused Page.navigate: 0x{0:X8}", errorCode);
            fallback();
        }
    }
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("5C4889F0-5EF6-4C5A-952C-D8F1B92D0574")]
internal partial interface IDevToolsMethodCompletedHandler
{
    void Invoke(int errorCode, nint returnObjectAsJson);
}
