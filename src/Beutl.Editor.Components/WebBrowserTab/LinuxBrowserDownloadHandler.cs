using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Avalonia.Platform;
using Avalonia.Threading;

using static Beutl.Editor.Components.WebBrowserTab.LinuxWebKit;

namespace Beutl.Editor.Components.WebBrowserTab;

// Avalonia's WebKitGTK adapter returns from response policy decisions without deciding, so WebKit uses every
// response and shows a file it cannot display, such as an archive, as text in place of the page. An emission hook
// sees each decision before the adapter's handler: it ignores such a main-frame file, which keeps the page and
// stops the transfer, and reports the file so that the downloader can request it instead.
[SupportedOSPlatform("linux")]
internal sealed partial class LinuxBrowserDownloadHandler : IDisposable
{
    private const int ResponseDecision = 2; // WEBKIT_POLICY_DECISION_TYPE_RESPONSE
    private static readonly Lock s_lock = new();
    private static readonly Dictionary<nint, LinuxBrowserDownloadHandler> s_handlers = [];
    private static bool s_hookInstalled;
    private readonly Api _api;
    private readonly nint _webView;
    private readonly Action<Uri, string?> _onDownload;
    private volatile bool _disposed;

    private LinuxBrowserDownloadHandler(Api api, nint webView, Action<Uri, string?> onDownload)
    {
        _api = api;
        _webView = webView;
        _onDownload = onDownload;
    }

    internal static LinuxBrowserDownloadHandler? TryAttach(IPlatformHandle? handle, Action<Uri, string?> onDownload)
    {
        // The WPE adapter decides responses itself.
        if (handle is not IGtkWebViewPlatformHandle { WebKitWebView: not 0 } gtk || Api.Gtk.Value is not { } api) return null;
        // Retain before dispatch; release on GLib too because the last unref can destroy a widget.
        var handler = new LinuxBrowserDownloadHandler(api, ObjectRef(gtk.WebKitWebView), onDownload);
        lock (s_lock) s_handlers[handler._webView] = handler;
        _ = OnGlibAsync(api.InstallHook);
        return handler;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (s_lock)
        {
            if (s_handlers.TryGetValue(_webView, out var current) && current == this) s_handlers.Remove(_webView);
        }
        _ = OnGlibAsync(() => ObjectUnref(_webView));
    }

    // Runs on the GLib thread during the decision, so it must not wait for the UI thread, which may be waiting for GLib.
    private void Decide(nint decision)
    {
        if (!_api.IsMainFrameResponse(decision) || _api.IsMimeTypeSupported(decision)) return;
        nint response = _api.GetResponse(decision);
        // The downloader requests the file again, which only reproduces a complete GET.
        if (_api.GetMethod(decision) != "GET" || _api.GetStatusCode(response) != 200
            || !Uri.TryCreate(_api.GetUri(response), UriKind.Absolute, out Uri? uri) || !BrowserMediaDownload.IsHttpUri(uri)) return;
        string? name = _api.GetSuggestedFilename(response);
        _api.Ignore(decision);
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed) _onDownload(uri, name);
        });
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnDecidePolicy(nint hint, uint count, nint values, nint data)
    {
        try
        {
            // decide-policy passes the web view, the decision and its type, each in a 24-byte GValue.
            if (count >= 3 && GetEnum(values + 48) == ResponseDecision)
            {
                LinuxBrowserDownloadHandler? handler;
                lock (s_lock) s_handlers.TryGetValue(GetObject(values), out handler);
                if (handler is { _disposed: false }) handler.Decide(GetObject(values + 24));
            }
        }
        catch
        {
            // Exceptions must not escape a native hook. WebKit then uses the response, as before.
        }
        return 1; // Keep the hook installed.
    }

    private sealed unsafe class Api
    {
        internal static readonly Lazy<Api?> Gtk = new(() =>
        {
            try { return new Api(LoadLibrary(GtkLibraries)); }
            // Without these exports the page shows the file as before.
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
        });
        private readonly delegate* unmanaged[Cdecl]<nuint> _getWebViewType;
        private readonly delegate* unmanaged[Cdecl]<nint, int> _isMainFrameResource, _isMimeTypeSupported;
        private readonly delegate* unmanaged[Cdecl]<nint, nint> _getRequest, _getResponse, _getMethod, _getUri, _getSuggestedFilename;
        private readonly delegate* unmanaged[Cdecl]<nint, uint> _getStatusCode;
        private readonly delegate* unmanaged[Cdecl]<nint, void> _ignore;

        private Api(nint webkit)
        {
            _getWebViewType = (delegate* unmanaged[Cdecl]<nuint>)NativeLibrary.GetExport(webkit, "webkit_web_view_get_type");
            // Since WebKit 2.40; earlier versions cannot tell a subframe's response apart.
            _isMainFrameResource = (delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(webkit, "webkit_response_policy_decision_is_main_frame_main_resource");
            _isMimeTypeSupported = (delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(webkit, "webkit_response_policy_decision_is_mime_type_supported");
            _getRequest = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit, "webkit_response_policy_decision_get_request");
            _getResponse = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit, "webkit_response_policy_decision_get_response");
            _getMethod = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit, "webkit_uri_request_get_http_method");
            _getUri = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit, "webkit_uri_response_get_uri");
            _getSuggestedFilename = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit, "webkit_uri_response_get_suggested_filename");
            _getStatusCode = (delegate* unmanaged[Cdecl]<nint, uint>)NativeLibrary.GetExport(webkit, "webkit_uri_response_get_status_code");
            _ignore = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(webkit, "webkit_policy_decision_ignore");
        }

        // Only touched on the GLib thread. The hook serves every web view and stays for the process.
        internal void InstallHook()
        {
            if (s_hookInstalled) return;
            uint signal = SignalLookup("decide-policy", _getWebViewType());
            if (signal == 0) return;
            AddEmissionHook(signal, 0, (nint)(delegate* unmanaged[Cdecl]<nint, uint, nint, nint, int>)&OnDecidePolicy, 0, 0);
            s_hookInstalled = true;
        }

        internal bool IsMainFrameResponse(nint decision) => _isMainFrameResource(decision) != 0;
        internal bool IsMimeTypeSupported(nint decision) => _isMimeTypeSupported(decision) != 0;
        internal nint GetResponse(nint decision) => _getResponse(decision);
        internal string? GetMethod(nint decision) => Marshal.PtrToStringUTF8(_getMethod(_getRequest(decision)));
        internal uint GetStatusCode(nint response) => _getStatusCode(response);
        internal string? GetUri(nint response) => Marshal.PtrToStringUTF8(_getUri(response));
        internal string? GetSuggestedFilename(nint response) => Marshal.PtrToStringUTF8(_getSuggestedFilename(response));
        internal void Ignore(nint decision) => _ignore(decision);
    }

    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_signal_lookup", StringMarshalling = StringMarshalling.Utf8)]
    private static partial uint SignalLookup(string name, nuint type);

    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_signal_add_emission_hook")]
    private static partial nuint AddEmissionHook(uint signal, uint detail, nint hook, nint data, nint destroy);

    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_value_get_object")]
    private static partial nint GetObject(nint value);

    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_value_get_enum")]
    private static partial int GetEnum(nint value);
}
