using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Avalonia.Threading;

using static Beutl.Editor.Components.WebBrowserTab.LinuxWebKit;

namespace Beutl.Editor.Components.WebBrowserTab;

internal sealed partial class LinuxBrowserDownloadHandler
{
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnDownloadStarted(nint hint, uint count, nint values, nint data)
    {
        try
        {
            // download-started passes the web context and the download, each in a 24-byte GValue.
            if (count >= 2 && Api.Gtk.Value is { } api)
            {
                nint download = GetObject(values + 24);
                LinuxBrowserDownloadHandler? handler;
                lock (s_lock) s_handlers.TryGetValue(api.GetDownloadWebView(download), out handler);
                string? address = api.GetDownloadUri(download);
                // A WebKit download names no frame, so only a blob: URL tells which document created the file.
                if (handler is { _disposed: false } && BrowserMediaDownload.IsGeneratedFileAddress(address))
                    GeneratedDownload.Adopt(handler, download, BrowserMediaDownload.GetGeneratedFileOrigin(address));
            }
        }
        catch
        {
            // Exceptions must not escape a native hook. WebKit then saves the file in the download directory, as before.
        }
        return 1; // Keep the hook installed.
    }

    // WebKit saves a download it starts in the user's download directory unless a handler decides its destination.
    // WebKitGTK 2.40+ lets a handler answer that decision later, so it waits here for the user's choice.
    private sealed class GeneratedDownload : IBrowserDownloadSource
    {
        private readonly LinuxBrowserDownloadHandler _owner;
        private readonly nint _download;
        private readonly Uri? _creator;
        private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Stopwatch _progressTimer = Stopwatch.StartNew();
        private GCHandle _self;
        private nuint[] _signals = [];
        private IProgress<(long Received, long? Total)>? _progress;
        private string? _fileName;
        private bool _nativeFinished;
        private bool _saving;
        private int _disposed;

        private GeneratedDownload(LinuxBrowserDownloadHandler owner, nint download, Uri? creator)
        {
            _owner = owner;
            // Released on the GLib thread, like the web view.
            _download = ObjectRef(download);
            _creator = creator;
        }

        public bool IsGenerated => true;

        private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        // Runs on the GLib thread, before WebKit asks for the destination.
        internal static unsafe void Adopt(LinuxBrowserDownloadHandler owner, nint download, Uri? creator)
        {
            var source = new GeneratedDownload(owner, download, creator);
            source._self = GCHandle.Alloc(source);
            nint data = GCHandle.ToIntPtr(source._self);
            source._signals =
            [
                SignalConnect(download, "decide-destination", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnDecideDestination, data, 0, 0),
                SignalConnect(download, "received-data", (nint)(delegate* unmanaged[Cdecl]<nint, ulong, nint, void>)&OnReceivedData, data, 0, 0),
                SignalConnect(download, "failed", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnFailed, data, 0, 0),
                SignalConnect(download, "finished", (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnFinished, data, 0, 0)
            ];
        }

        public async Task<string> SaveAsync(string directory, IProgress<(long Received, long? Total)>? progress,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_saving) throw new InvalidOperationException("The native response can only be saved once.");
            _saving = true;
            _progress = progress;
            (string stagingDirectory, string temporaryPath) = BrowserMediaDownload.CreateStagingPaths(directory);
            try
            {
                Directory.CreateDirectory(stagingDirectory);
                using (cancellationToken.Register(Dispose))
                {
                    await OnGlibAsync(() =>
                    {
                        if (!IsDisposed) _owner._api.SetDestination(_download, temporaryPath);
                    });
                    await _finished.Task.WaitAsync(cancellationToken);
                }
                return await BrowserMediaDownload.PublishNativeFileAsync(temporaryPath, directory, _fileName!, IsGenerated, null,
                    cancellationToken);
            }
            finally
            {
                Dispose();
                // WebKit may still hold a canceled file; a failed cleanup must not hide the result.
                try { Directory.Delete(stagingDirectory, true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _finished.TrySetCanceled();
            if (_finished.Task.IsFaulted) _ = _finished.Task.Exception;
            _ = OnGlibAsync(Release);
        }

        // Disconnects first, so that canceling reports nothing and no callback outlives the handle.
        private void Release()
        {
            foreach (nuint signal in _signals) SignalHandlerDisconnect(_download, signal);
            _signals = [];
            if (!_nativeFinished) _owner._api.Cancel(_download);
            ObjectUnref(_download);
            _self.Free();
        }

        private static GeneratedDownload FromHandle(nint data) => (GeneratedDownload)GCHandle.FromIntPtr(data).Target!;

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static int OnDecideDestination(nint download, nint suggestedFilename, nint data)
        {
            GeneratedDownload? source = null;
            try
            {
                source = FromHandle(data);
                LinuxBrowserDownloadHandler owner = source._owner;
                nint response = owner._api.GetDownloadResponse(download);
                string name = BrowserMediaDownload.CreateGeneratedFileName(Marshal.PtrToStringUTF8(suggestedFilename),
                    response != 0 ? owner._api.GetMimeType(response) : null);
                source._fileName = name;
                GeneratedDownload offered = source;
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        if (owner._disposed || offered.IsDisposed) offered.Dispose();
                        else owner._onGeneratedDownload(offered._creator, name, offered, null);
                    }
                    catch
                    {
                        offered.Dispose();
                    }
                });
            }
            catch
            {
                source?.Dispose();
            }
            // Handled either way: the user's choice sets the destination later, or the download is canceled.
            return 1;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnReceivedData(nint download, ulong length, nint data)
        {
            try
            {
                GeneratedDownload source = FromHandle(data);
                if (source._progress is not { } progress || source._progressTimer.ElapsedMilliseconds < 100) return;
                source._progressTimer.Restart();
                nint response = source._owner._api.GetDownloadResponse(download);
                ulong total = response != 0 ? source._owner._api.GetContentLength(response) : 0;
                progress.Report(((long)source._owner._api.GetReceivedDataLength(download), total > 0 ? (long)total : null));
            }
            catch
            {
                // Progress is advisory; the transfer still finishes or fails on its own.
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static unsafe void OnFailed(nint download, nint error, nint data)
        {
            try
            {
                string? message = error != 0 ? Marshal.PtrToStringUTF8(((GError*)error)->Message) : null;
                FromHandle(data)._finished.TrySetException(new IOException(message ?? Strings.WebDownloadIncomplete));
            }
            catch
            {
                // Exceptions must not escape a native callback; "finished" still follows.
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnFinished(nint download, nint data)
        {
            try
            {
                // WebKit also emits this after "failed", which has already settled the result.
                GeneratedDownload source = FromHandle(data);
                source._nativeFinished = true;
                source._finished.TrySetResult();
                // A download that ended before WebKit named it was never offered, so no one else releases it.
                if (source._fileName == null) source.Dispose();
            }
            catch
            {
                // Exceptions must not escape a native callback.
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GError
    {
        public uint Domain;
        public int Code;
        public nint Message;
    }

    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_signal_connect_data", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nuint SignalConnect(nint instance, string signal, nint handler, nint data, nint destroy, int flags);

    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_signal_handler_disconnect")]
    private static partial void SignalHandlerDisconnect(nint instance, nuint handler);

    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_filename_to_uri", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint FilenameToUri(string filename, nint hostname, nint error);

    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_free")]
    private static partial void Free(nint value);
}
