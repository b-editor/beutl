using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using static Beutl.Editor.Components.WebBrowserTab.LinuxWebKit;

namespace Beutl.Editor.Components.WebBrowserTab;

// NativeWebView.Navigate sends no request headers. WebKitGTK and WPE load a request whose headers name the referrer.
[SupportedOSPlatform("linux")]
internal static class LinuxReferrerNavigation
{
    internal static bool TryNavigate(nint webView, bool wpe, Uri uri, string referrer)
    {
        if (webView == 0 || (wpe ? Api.Wpe : Api.Gtk).Value is not { } api) return false;
        _ = OnGlibAsync(webView, () => api.Load(webView, uri.AbsoluteUri, referrer));
        return true;
    }

    private sealed unsafe class Api
    {
        internal static readonly Lazy<Api?> Gtk = new(() => Create(wpe: false));
        internal static readonly Lazy<Api?> Wpe = new(() => Create(wpe: true));
        private readonly delegate* unmanaged[Cdecl]<nint, nint> _createRequest, _getHeaders;
        private readonly delegate* unmanaged[Cdecl]<nint, nint, nint, void> _appendHeader;
        private readonly delegate* unmanaged[Cdecl]<nint, nint, void> _loadRequest;

        private Api(nint webkit, nint soup)
        {
            _createRequest = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit, "webkit_uri_request_new");
            _getHeaders = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit, "webkit_uri_request_get_http_headers");
            _appendHeader = (delegate* unmanaged[Cdecl]<nint, nint, nint, void>)NativeLibrary.GetExport(soup, "soup_message_headers_append");
            _loadRequest = (delegate* unmanaged[Cdecl]<nint, nint, void>)NativeLibrary.GetExport(webkit, "webkit_web_view_load_request");
        }

        private static Api? Create(bool wpe)
        {
            try
            {
                (nint webkit, nint soup) = LoadWithSoup(wpe);
                return new Api(webkit, soup);
            }
            // Without these exports the tab navigates without the referrer, as before.
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
        }

        // Only called on the GLib thread.
        internal void Load(nint webView, string uri, string referrer)
        {
            nint address = Marshal.StringToCoTaskMemUTF8(uri);
            nint name = Marshal.StringToCoTaskMemUTF8("Referer");
            nint value = Marshal.StringToCoTaskMemUTF8(referrer);
            nint request = 0;
            try
            {
                request = _createRequest(address);
                // WebKit gives an HTTP request its headers; any other address has none.
                nint headers = _getHeaders(request);
                if (headers != 0) _appendHeader(headers, name, value);
                _loadRequest(webView, request);
            }
            finally
            {
                if (request != 0) ObjectUnref(request);
                Marshal.FreeCoTaskMem(value);
                Marshal.FreeCoTaskMem(name);
                Marshal.FreeCoTaskMem(address);
            }
        }
    }
}
