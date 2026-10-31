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
// A file a page script generated cannot be requested again, so its download is taken from WebKit instead.
// A page's window.open() only emits create, which the adapter does not handle, so a hook reports its page as well.
[SupportedOSPlatform("linux")]
internal sealed partial class LinuxBrowserDownloadHandler : IDisposable
{
    private const int NewWindowDecision = 1; // WEBKIT_POLICY_DECISION_TYPE_NEW_WINDOW_ACTION
    private const int ResponseDecision = 2; // WEBKIT_POLICY_DECISION_TYPE_RESPONSE
    private static readonly Lock s_lock = new();
    private static readonly Dictionary<nint, LinuxBrowserDownloadHandler> s_handlers = [];
    private static bool s_policyHookInstalled;
    private static bool s_downloadHookInstalled;
    private static bool s_createHookInstalled;
    private readonly Api _api;
    private readonly nint _webView;
    private readonly Action<Uri, string?> _onDownload;
    private readonly Action<Uri?, string, IBrowserDownloadSource, Uri?> _onGeneratedDownload;
    private readonly Action<Uri> _onWindowOpened;
    private volatile bool _disposed;

    private LinuxBrowserDownloadHandler(Api api, nint webView, Action<Uri, string?> onDownload,
        Action<Uri?, string, IBrowserDownloadSource, Uri?> onGeneratedDownload, Action<Uri> onWindowOpened)
    {
        _api = api;
        _webView = webView;
        _onDownload = onDownload;
        _onGeneratedDownload = onGeneratedDownload;
        _onWindowOpened = onWindowOpened;
    }

    internal static LinuxBrowserDownloadHandler? TryAttach(IPlatformHandle? handle, Action<Uri, string?> onDownload,
        Action<Uri?, string, IBrowserDownloadSource, Uri?> onGeneratedDownload, Action<Uri> onWindowOpened)
    {
        // The WPE adapter decides responses itself.
        if (handle is not IGtkWebViewPlatformHandle { WebKitWebView: not 0 } gtk || Api.Gtk.Value is not { } api) return null;
        // Retain before dispatch; release on GLib too because the last unref can destroy a widget.
        var handler = new LinuxBrowserDownloadHandler(api, ObjectRef(gtk.WebKitWebView), onDownload, onGeneratedDownload,
            onWindowOpened);
        lock (s_lock) s_handlers[handler._webView] = handler;
        _ = OnGlibAsync(api.InstallHooks);
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
            int type = count >= 3 ? GetEnum(values + 48) : 0;
            if (type is ResponseDecision or NewWindowDecision)
            {
                LinuxBrowserDownloadHandler? handler;
                lock (s_lock) s_handlers.TryGetValue(GetObject(values), out handler);
                if (handler is not { _disposed: false }) return 1;
                if (type == ResponseDecision) handler.Decide(GetObject(values + 24));
                // The adapter opens a link's new window as a tab through NewWindowRequested but decides nothing, so
                // WebKit would use the decision and emit create, whose hook would open the tab a second time.
                else handler._api.Ignore(GetObject(values + 24));
            }
        }
        catch
        {
            // Exceptions must not escape a native hook. WebKit then uses the response, as before.
        }
        return 1; // Keep the hook installed.
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnCreate(nint hint, uint count, nint values, nint data)
    {
        try
        {
            // create passes the web view and the navigation action, each in a 24-byte GValue. The web view it asks
            // for is never made, so the page gets null back from window.open().
            if (count >= 2 && Api.Gtk.Value is { } api)
            {
                LinuxBrowserDownloadHandler? handler;
                lock (s_lock) s_handlers.TryGetValue(GetObject(values), out handler);
                if (handler is { _disposed: false }
                    && Uri.TryCreate(api.GetActionUri(GetBoxed(values + 24)), UriKind.Absolute, out Uri? uri)
                    && BrowserMediaDownload.IsHttpUri(uri))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!handler._disposed) handler._onWindowOpened(uri);
                    });
                }
            }
        }
        catch
        {
            // Exceptions must not escape a native hook. The page then opens nothing, as before.
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
        private readonly delegate* unmanaged[Cdecl]<nuint> _getWebViewType, _getWebContextType;
        private readonly delegate* unmanaged[Cdecl]<nint, int> _isMainFrameResource, _isMimeTypeSupported;
        private readonly delegate* unmanaged[Cdecl]<nint, nint> _getRequest, _getResponse, _getMethod, _getUri, _getSuggestedFilename;
        private readonly delegate* unmanaged[Cdecl]<nint, uint> _getStatusCode;
        private readonly delegate* unmanaged[Cdecl]<nint, void> _ignore;
        private readonly delegate* unmanaged[Cdecl]<nint, nint> _getDownloadWebView, _getDownloadRequest, _getDownloadResponse, _getRequestUri, _getMimeType;
        private readonly delegate* unmanaged[Cdecl]<nint, ulong> _getReceivedDataLength, _getContentLength;
        private readonly delegate* unmanaged[Cdecl]<nint, nint, void> _setDestination;
        private readonly delegate* unmanaged[Cdecl]<nint, void> _cancel;
        private readonly delegate* unmanaged[Cdecl]<nint, nint> _getActionRequest;

        private Api(nint webkit)
        {
            _getWebViewType = (delegate* unmanaged[Cdecl]<nuint>)NativeLibrary.GetExport(webkit, "webkit_web_view_get_type");
            _getWebContextType = (delegate* unmanaged[Cdecl]<nuint>)NativeLibrary.GetExport(webkit, "webkit_web_context_get_type");
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
            _getDownloadWebView = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit, "webkit_download_get_web_view");
            _getDownloadRequest = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit, "webkit_download_get_request");
            _getDownloadResponse = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit, "webkit_download_get_response");
            _getRequestUri = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit, "webkit_uri_request_get_uri");
            _getMimeType = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit, "webkit_uri_response_get_mime_type");
            _getReceivedDataLength = (delegate* unmanaged[Cdecl]<nint, ulong>)NativeLibrary.GetExport(webkit, "webkit_download_get_received_data_length");
            _getContentLength = (delegate* unmanaged[Cdecl]<nint, ulong>)NativeLibrary.GetExport(webkit, "webkit_uri_response_get_content_length");
            _setDestination = (delegate* unmanaged[Cdecl]<nint, nint, void>)NativeLibrary.GetExport(webkit, "webkit_download_set_destination");
            _cancel = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(webkit, "webkit_download_cancel");
            _getActionRequest = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit, "webkit_navigation_action_get_request");
        }

        // Only touched on the GLib thread. The hooks serve every web view and stay for the process.
        internal void InstallHooks()
        {
            if (!s_policyHookInstalled)
            {
                uint signal = SignalLookup("decide-policy", _getWebViewType());
                if (signal != 0) AddEmissionHook(signal, 0, (nint)(delegate* unmanaged[Cdecl]<nint, uint, nint, nint, int>)&OnDecidePolicy, 0, 0);
                s_policyHookInstalled = signal != 0;
            }
            if (!s_downloadHookInstalled)
            {
                uint signal = SignalLookup("download-started", _getWebContextType());
                if (signal != 0) AddEmissionHook(signal, 0, (nint)(delegate* unmanaged[Cdecl]<nint, uint, nint, nint, int>)&OnDownloadStarted, 0, 0);
                s_downloadHookInstalled = signal != 0;
            }
            if (!s_createHookInstalled)
            {
                uint signal = SignalLookup("create", _getWebViewType());
                if (signal != 0) AddEmissionHook(signal, 0, (nint)(delegate* unmanaged[Cdecl]<nint, uint, nint, nint, int>)&OnCreate, 0, 0);
                s_createHookInstalled = signal != 0;
            }
        }

        internal bool IsMainFrameResponse(nint decision) => _isMainFrameResource(decision) != 0;
        internal bool IsMimeTypeSupported(nint decision) => _isMimeTypeSupported(decision) != 0;
        internal nint GetResponse(nint decision) => _getResponse(decision);
        internal string? GetMethod(nint decision) => Marshal.PtrToStringUTF8(_getMethod(_getRequest(decision)));
        internal uint GetStatusCode(nint response) => _getStatusCode(response);
        internal string? GetUri(nint response) => Marshal.PtrToStringUTF8(_getUri(response));
        internal string? GetSuggestedFilename(nint response) => Marshal.PtrToStringUTF8(_getSuggestedFilename(response));
        internal void Ignore(nint decision) => _ignore(decision);
        internal nint GetDownloadWebView(nint download) => _getDownloadWebView(download);
        internal string? GetDownloadUri(nint download) => Marshal.PtrToStringUTF8(_getRequestUri(_getDownloadRequest(download)));
        internal nint GetDownloadResponse(nint download) => _getDownloadResponse(download);
        internal string? GetMimeType(nint response) => Marshal.PtrToStringUTF8(_getMimeType(response));
        internal ulong GetReceivedDataLength(nint download) => _getReceivedDataLength(download);
        internal ulong GetContentLength(nint response) => _getContentLength(response);
        internal void Cancel(nint download) => _cancel(download);
        internal string? GetActionUri(nint action) => Marshal.PtrToStringUTF8(_getRequestUri(_getActionRequest(action)));

        // The WebKitGTK 4.x API takes a file URI.
        internal void SetDestination(nint download, string path)
        {
            nint uri = FilenameToUri(path, 0, 0);
            if (uri == 0) throw new IOException(Strings.WebDownloadIncomplete);
            try { _setDestination(download, uri); }
            finally { Free(uri); }
        }
    }

    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_signal_lookup", StringMarshalling = StringMarshalling.Utf8)]
    private static partial uint SignalLookup(string name, nuint type);

    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_signal_add_emission_hook")]
    private static partial nuint AddEmissionHook(uint signal, uint detail, nint hook, nint data, nint destroy);

    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_value_get_object")]
    private static partial nint GetObject(nint value);

    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_value_get_enum")]
    private static partial int GetEnum(nint value);

    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_value_get_boxed")]
    private static partial nint GetBoxed(nint value);
}
