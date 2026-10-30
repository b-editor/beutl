using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Avalonia.Platform;

using static Beutl.Editor.Components.WebBrowserTab.LinuxWebKit;

namespace Beutl.Editor.Components.WebBrowserTab;

// WebKitGTK and WPE have no native download handler, so a download starts here through WebKit's own network
// session. Like the macOS and Windows native downloads, the request then carries the page's cookies, user agent
// and connection; sites behind bot protection reject the same request from a separate HttpClient.
internal sealed class LinuxBrowserDownloadSource : IBrowserDownloadSource
{
    private readonly Api _api;
    private readonly nint _webView;
    private readonly Uri _uri;
    private readonly string? _suggestedName;
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Stopwatch _progressTimer = new();
    private IProgress<(long Received, long? Total)>? _progress;
    private GCHandle _self;
    private nint _download;
    private nuint[] _handlers = [];
    private string? _destination;
    private string? _fileName;
    private bool _nativeFinished;
    private bool _saving;
    private bool _disposed;

    private LinuxBrowserDownloadSource(Api api, nint webView, Uri uri, string? suggestedName)
    {
        _api = api;
        // Released on the GLib thread, because the last reference can destroy a widget.
        _webView = ObjectRef(webView);
        _uri = uri;
        _suggestedName = suggestedName;
    }

    internal static IBrowserDownloadSource? TryCreate(IPlatformHandle? handle, Uri uri, string? suggestedName) => handle switch
    {
        IGtkWebViewPlatformHandle gtk when OperatingSystem.IsLinux() => new LinuxBrowserDownloadSource(Api.Gtk.Value, gtk.WebKitWebView, uri, suggestedName),
        ILinuxWpePlatformHandle wpe when OperatingSystem.IsLinux() => new LinuxBrowserDownloadSource(Api.Wpe.Value, wpe.WebKitWebView, uri, suggestedName),
        _ => null
    };

    public async Task<string> SaveAsync(string directory, IProgress<(long Received, long? Total)>? progress,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_saving) throw new InvalidOperationException("A WebKit download can only be saved once.");
        _saving = true;
        _progress = progress;
        string stagingDirectory = Path.Combine(Path.GetFullPath(directory), $".beutl-download-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        _destination = Path.Combine(stagingDirectory, "response");
        try
        {
            await OnGlibAsync(Start);
            using (cancellationToken.Register(() =>
                   {
                       _finished.TrySetCanceled(cancellationToken);
                       _ = OnGlibAsync(Cancel);
                   }))
            {
                await _finished.Task;
            }
            return await BrowserMediaDownload.ValidateAndPublishAsync(_destination, directory,
                _fileName ?? throw new IOException(Strings.WebDownloadIncomplete), null, cancellationToken);
        }
        finally
        {
            // WebKit may still hold a canceled response; a later cleanup of the staging folder must not hide the failure.
            try { Directory.Delete(stagingDirectory, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _finished.TrySetCanceled();
        if (_finished.Task.IsFaulted) _ = _finished.Task.Exception;
        _ = OnGlibAsync(Release);
    }

    private unsafe void Start()
    {
        _self = GCHandle.Alloc(this);
        _download = _api.DownloadUri(_webView, _uri.AbsoluteUri);
        if (_download == 0) throw new IOException(Strings.WebDownloadIncomplete);
        nint data = GCHandle.ToIntPtr(_self);
        _handlers =
        [
            SignalConnect(_download, "decide-destination", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnDecideDestination, data, 0, 0),
            SignalConnect(_download, "received-data", (nint)(delegate* unmanaged[Cdecl]<nint, ulong, nint, void>)&OnReceivedData, data, 0, 0),
            SignalConnect(_download, "failed", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnFailed, data, 0, 0),
            SignalConnect(_download, "finished", (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnFinished, data, 0, 0)
        ];
        _progressTimer.Start();
    }

    private void Cancel()
    {
        if (_download != 0 && !_nativeFinished) _api.Cancel(_download);
    }

    private void Release()
    {
        if (_download != 0)
        {
            foreach (nuint handler in _handlers) SignalDisconnect(_download, handler);
            Cancel();
            ObjectUnref(_download);
            _download = 0;
        }
        if (_self.IsAllocated) _self.Free();
        ObjectUnref(_webView);
    }

    // The response is known here, before WebKit writes anything to disk.
    private void ChooseDestination(nint download, string? suggestedFilename)
    {
        nint response = _api.GetResponse(download);
        if (response == 0) throw new IOException(Strings.WebDownloadIncomplete);
        uint status = _api.GetStatusCode(response);
        // A partial response would be published as a complete file.
        if (status is >= 400 or 206) throw new ResponseRejectedException($"WebKit received HTTP status {status}.");
        Uri responseUri = Uri.TryCreate(_api.GetUri(response), UriKind.Absolute, out Uri? finalUri)
            ? finalUri : _uri;
        string? nameHint = _api.GetSuggestedFilename(response) ?? _suggestedName ?? suggestedFilename;
        _fileName = BrowserMediaDownload.GetResponseFileName(responseUri, nameHint, _api.GetMimeType(response))
            ?? throw new ResponseRejectedException(Strings.WebDownloadUnsupported);
        _api.SetDestination(download, _destination!);
    }

    private static LinuxBrowserDownloadSource GetSource(nint data) => (LinuxBrowserDownloadSource)GCHandle.FromIntPtr(data).Target!;

    // WebKit cannot send a referrer with a download it starts, so sites that require one reject it. The caller
    // retries such a response without WebKit; it is canceled before WebKit writes anything.
    internal sealed class ResponseRejectedException(string message) : IOException(message);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnDecideDestination(nint download, nint suggestedFilename, nint data)
    {
        LinuxBrowserDownloadSource source = GetSource(data);
        try { source.ChooseDestination(download, Marshal.PtrToStringUTF8(suggestedFilename)); }
        catch (Exception ex)
        {
            source._finished.TrySetException(ex);
            source._api.Cancel(download);
        }
        // Handled either way: WebKit must never fall back to the user's download directory.
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnReceivedData(nint download, ulong length, nint data)
    {
        LinuxBrowserDownloadSource source = GetSource(data);
        if (source._progress is not { } progress || source._progressTimer.ElapsedMilliseconds < 100) return;
        source._progressTimer.Restart();
        try
        {
            nint response = source._api.GetResponse(download);
            ulong total = response != 0 ? source._api.GetContentLength(response) : 0;
            progress.Report(((long)source._api.GetReceivedDataLength(download), total > 0 ? (long)total : null));
        }
        catch (Exception ex) { source._finished.TrySetException(ex); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnFailed(nint download, nint error, nint data)
    {
        string? message = error != 0 ? Marshal.PtrToStringUTF8(((GError*)error)->Message) : null;
        GetSource(data)._finished.TrySetException(new IOException(message ?? Strings.WebDownloadIncomplete));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnFinished(nint download, nint data)
    {
        // WebKit also emits this after "failed", which has already settled the result.
        LinuxBrowserDownloadSource source = GetSource(data);
        source._nativeFinished = true;
        source._finished.TrySetResult();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GError
    {
        public uint Domain;
        public int Code;
        public nint Message;
    }

    private sealed unsafe class Api
    {
        internal static readonly Lazy<Api> Gtk = new(() => new(GtkLibraries, false));
        internal static readonly Lazy<Api> Wpe = new(() => new(WpeLibraries, true));
        private readonly bool _wpe;
        private readonly delegate* unmanaged[Cdecl]<nint, nint, nint> _downloadUri;
        private readonly delegate* unmanaged[Cdecl]<nint, nint, void> _setDestination;
        private readonly delegate* unmanaged[Cdecl]<nint, nint> _getResponse;
        private readonly delegate* unmanaged[Cdecl]<nint, ulong> _getReceivedDataLength;
        private readonly delegate* unmanaged[Cdecl]<nint, void> _cancel;
        private readonly delegate* unmanaged[Cdecl]<nint, uint> _getStatusCode;
        private readonly delegate* unmanaged[Cdecl]<nint, nint> _getUri, _getMimeType, _getSuggestedFilename;
        private readonly delegate* unmanaged[Cdecl]<nint, ulong> _getContentLength;

        private Api(string[] libraries, bool wpe)
        {
            nint library = LoadLibrary(libraries);
            nint Export(string name) => NativeLibrary.GetExport(library, name);
            _wpe = wpe;
            _downloadUri = (delegate* unmanaged[Cdecl]<nint, nint, nint>)Export("webkit_web_view_download_uri");
            _setDestination = (delegate* unmanaged[Cdecl]<nint, nint, void>)Export("webkit_download_set_destination");
            _getResponse = (delegate* unmanaged[Cdecl]<nint, nint>)Export("webkit_download_get_response");
            _getReceivedDataLength = (delegate* unmanaged[Cdecl]<nint, ulong>)Export("webkit_download_get_received_data_length");
            _cancel = (delegate* unmanaged[Cdecl]<nint, void>)Export("webkit_download_cancel");
            _getStatusCode = (delegate* unmanaged[Cdecl]<nint, uint>)Export("webkit_uri_response_get_status_code");
            _getUri = (delegate* unmanaged[Cdecl]<nint, nint>)Export("webkit_uri_response_get_uri");
            _getMimeType = (delegate* unmanaged[Cdecl]<nint, nint>)Export("webkit_uri_response_get_mime_type");
            _getSuggestedFilename = (delegate* unmanaged[Cdecl]<nint, nint>)Export("webkit_uri_response_get_suggested_filename");
            _getContentLength = (delegate* unmanaged[Cdecl]<nint, ulong>)Export("webkit_uri_response_get_content_length");
        }

        internal nint DownloadUri(nint webView, string uri)
        {
            nint text = Marshal.StringToCoTaskMemUTF8(uri);
            try { return _downloadUri(webView, text); }
            finally { Marshal.FreeCoTaskMem(text); }
        }

        // The WPE 2.0 API takes a path; the WebKitGTK 4.x API takes a file URI, and older releases accept nothing else.
        internal void SetDestination(nint download, string path)
        {
            nint text = Marshal.StringToCoTaskMemUTF8(_wpe ? path : new Uri(path).AbsoluteUri);
            try { _setDestination(download, text); }
            finally { Marshal.FreeCoTaskMem(text); }
        }

        internal nint GetResponse(nint download) => _getResponse(download);
        internal ulong GetReceivedDataLength(nint download) => _getReceivedDataLength(download);
        internal void Cancel(nint download) => _cancel(download);
        internal uint GetStatusCode(nint response) => _getStatusCode(response);
        internal string? GetUri(nint response) => Marshal.PtrToStringUTF8(_getUri(response));
        internal string? GetMimeType(nint response) => Marshal.PtrToStringUTF8(_getMimeType(response));
        internal string? GetSuggestedFilename(nint response) => Marshal.PtrToStringUTF8(_getSuggestedFilename(response));
        internal ulong GetContentLength(nint response) => _getContentLength(response);
    }
}
