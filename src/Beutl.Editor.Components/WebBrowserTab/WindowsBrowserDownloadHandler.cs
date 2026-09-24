using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

using Avalonia.Platform;
using Avalonia.Threading;

namespace Beutl.Editor.Components.WebBrowserTab;

// WebView2 keeps the response and its POST/redirect context. Hold its download
// event until the user chooses a destination, then save that same transfer.
internal sealed partial class WindowsBrowserDownloadHandler : IDisposable
{
    private readonly Action<Uri, string, IBrowserDownloadSource> _onDownload;
    private readonly DownloadStartingHandler _handler;
    private readonly HashSet<NativeDownload> _downloads = [];
    private nint _webView;
    private long _token;
    private bool _subscribed;

    private unsafe WindowsBrowserDownloadHandler(IWindowsWebView2PlatformHandle handle,
        Action<Uri, string, IBrowserDownloadSource> onDownload)
    {
        _onDownload = onDownload;
        _handler = new DownloadStartingHandler(this);
        nint coreWebView = handle.CoreWebView2;
        if (coreWebView == 0) throw new InvalidOperationException("WebView2 is not available.");
        try
        {
            var iid = new Guid("20D02D59-6DF2-42DC-BD06-F98A694B1302"); // ICoreWebView2_4
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(coreWebView, in iid, out _webView));
            void* handler = ComInterfaceMarshaller<IDownloadStartingHandler>.ConvertToUnmanaged(_handler);
            try
            {
                // ICoreWebView2_4::add_DownloadStarting
                long token;
                Marshal.ThrowExceptionForHR(
                    ((delegate* unmanaged[Stdcall]<nint, void*, long*, int>)Method(_webView, 75))(_webView, handler, &token));
                _token = token;
                _subscribed = true;
            }
            finally
            {
                ComInterfaceMarshaller<IDownloadStartingHandler>.Free(handler);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            Marshal.Release(coreWebView);
        }
    }

    internal static WindowsBrowserDownloadHandler? TryAttach(IPlatformHandle? handle,
        Action<Uri, string, IBrowserDownloadSource> onDownload)
    {
        if (handle is not IWindowsWebView2PlatformHandle windows) return null;
        try { return new WindowsBrowserDownloadHandler(windows, onDownload); }
        catch (Exception ex)
        {
            Trace.TraceError("WebView2 download handler could not be installed: {0}", ex);
            return null;
        }
    }

    private unsafe void OnDownloadStarting(nint args)
    {
        nint operation = 0;
        nint deferral = 0;
        NativeDownload? source = null;
        try
        {
            operation = GetObject(args, 3);
            string? address = GetString(operation, 9);
            if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)) return;
            string? mimeType = GetString(operation, 11)?.Split(';')[0].Trim();
            string? suggestedPath = GetString(args, 6);
            string? name = BrowserMediaDownload.GetResponseFileName(uri, Path.GetFileName(suggestedPath), mimeType);
            if (name == null) return;

            deferral = GetObject(args, 10);
            Marshal.AddRef(args);
            source = new NativeDownload(this, args, operation, deferral, name);
            operation = deferral = 0;
            source.HideDefaultDialog();
            _downloads.Add(source);
            _onDownload(uri, name, source);
        }
        catch (Exception ex)
        {
            Trace.TraceError("WebView2 download could not be offered: {0}", ex);
            source?.Dispose();
        }
        finally
        {
            if (deferral != 0)
            {
                // Never fall through to the browser's default save path after an error.
                _ = ((delegate* unmanaged[Stdcall]<nint, int, int>)Method(args, 5))(args, 1);
                _ = ((delegate* unmanaged[Stdcall]<nint, int>)Method(deferral, 3))(deferral);
                Marshal.Release(deferral);
            }
            if (operation != 0) Marshal.Release(operation);
        }
    }

    public unsafe void Dispose()
    {
        if (_webView == 0) return;
        if (_subscribed)
        {
            _ = ((delegate* unmanaged[Stdcall]<nint, long, int>)Method(_webView, 76))(_webView, _token);
            _subscribed = false;
        }
        foreach (NativeDownload download in _downloads.ToArray()) download.Dispose();
        Marshal.Release(_webView);
        _webView = 0;
    }

    private static unsafe nint Method(nint instance, int slot) => (*(nint**)instance)[slot];

    private static unsafe nint GetObject(nint instance, int slot)
    {
        nint result;
        Marshal.ThrowExceptionForHR(
            ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Method(instance, slot))(instance, &result));
        return result;
    }

    private static string? GetString(nint instance, int slot)
    {
        nint value = GetObject(instance, slot);
        try { return Marshal.PtrToStringUni(value); }
        finally { if (value != 0) Marshal.FreeCoTaskMem(value); }
    }

    [GeneratedComClass]
    private sealed partial class DownloadStartingHandler(WindowsBrowserDownloadHandler owner) : IDownloadStartingHandler
    {
        public void Invoke(nint sender, nint args) => owner.OnDownloadStarting(args);
    }

    private sealed class NativeDownload(
        WindowsBrowserDownloadHandler owner, nint args, nint operation, nint deferral, string fileName) : IBrowserDownloadSource
    {
        private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private nint _args = args;
        private nint _operation = operation;
        private nint _deferral = deferral;
        private DispatcherTimer? _progressTimer;
        private IProgress<(long Received, long? Total)>? _progress;
        private bool _saving;
        private bool _disposed;

        internal unsafe void HideDefaultDialog() =>
            Marshal.ThrowExceptionForHR(
                ((delegate* unmanaged[Stdcall]<nint, int, int>)Method(_args, 9))(_args, 1));

        public async Task<string> SaveAsync(string directory, IProgress<(long Received, long? Total)>? progress,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_saving) throw new InvalidOperationException("The native response can only be saved once.");
            _saving = true;
            _progress = progress;
            using var registration = cancellationToken.Register(() =>
            {
                if (Dispatcher.UIThread.CheckAccess()) Dispose();
                else Dispatcher.UIThread.Post(Dispose);
            });

            string stagingDirectory = Path.Combine(Path.GetFullPath(directory), $".beutl-download-{Guid.NewGuid():N}");
            string temporaryPath = Path.Combine(stagingDirectory, "response");
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(stagingDirectory);
                SetResultFilePath(temporaryPath);

                _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _progressTimer.Tick += OnProgressTick;
                _progressTimer.Start();
                CompleteDeferral();
                await _finished.Task.WaitAsync(cancellationToken);
                _progressTimer.Stop();
                return await BrowserMediaDownload.ValidateAndPublishAsync(
                    temporaryPath, directory, fileName, null, cancellationToken);
            }
            finally
            {
                // Release WebView2's file handle before removing a canceled or failed transfer.
                Dispose();
                await DeleteStagingDirectoryAsync(stagingDirectory);
            }
        }

        private static async Task DeleteStagingDirectoryAsync(string path)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                if (!Directory.Exists(path)) return;
                try
                {
                    Directory.Delete(path, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < 9)
                {
                    // WebView2 can finish closing a canceled file after the operation is released.
                    await Task.Delay(100);
                }
                catch (IOException)
                {
                    Trace.TraceWarning("WebView2 staging cleanup could not complete after cancellation.");
                }
            }
        }

        private unsafe void SetResultFilePath(string path)
        {
            fixed (char* value = path)
            {
                // ICoreWebView2DownloadStartingEventArgs::put_ResultFilePath
                Marshal.ThrowExceptionForHR(
                    ((delegate* unmanaged[Stdcall]<nint, char*, int>)Method(_args, 7))(_args, value));
            }
        }

        private unsafe void OnProgressTick(object? sender, EventArgs e)
        {
            if (_disposed || _operation == 0) return;
            try
            {
                long received, total;
                int state;
                Marshal.ThrowExceptionForHR(
                    ((delegate* unmanaged[Stdcall]<nint, long*, int>)Method(_operation, 13))(_operation, &received));
                Marshal.ThrowExceptionForHR(
                    ((delegate* unmanaged[Stdcall]<nint, long*, int>)Method(_operation, 12))(_operation, &total));
                _progress?.Report((Math.Max(0, received), total >= 0 ? total : null));
                Marshal.ThrowExceptionForHR(
                    ((delegate* unmanaged[Stdcall]<nint, int*, int>)Method(_operation, 16))(_operation, &state));
                if (state == 2) _finished.TrySetResult(); // COREWEBVIEW2_DOWNLOAD_STATE_COMPLETED
                else if (state == 1) _finished.TrySetException(new IOException(Strings.WebDownloadIncomplete));
            }
            catch (Exception ex)
            {
                _finished.TrySetException(ex);
            }
        }

        private unsafe void CompleteDeferral()
        {
            nint deferral = _deferral;
            _deferral = 0;
            try
            {
                Marshal.ThrowExceptionForHR(
                    ((delegate* unmanaged[Stdcall]<nint, int>)Method(deferral, 3))(deferral));
            }
            finally
            {
                Marshal.Release(deferral);
                Marshal.Release(_args);
                _args = 0;
            }
        }

        public unsafe void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _progressTimer?.Stop();
            bool completed = _finished.Task.IsCompletedSuccessfully;
            _finished.TrySetCanceled();
            try
            {
                if (_deferral != 0)
                {
                    try { _ = ((delegate* unmanaged[Stdcall]<nint, int, int>)Method(_args, 5))(_args, 1); }
                    finally { CompleteDeferral(); }
                }
                else if (_operation != 0 && !completed)
                {
                    _ = ((delegate* unmanaged[Stdcall]<nint, int>)Method(_operation, 18))(_operation);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError("WebView2 download could not be canceled: {0}", ex);
            }
            finally
            {
                if (_operation != 0) Marshal.Release(_operation);
                _operation = 0;
                owner._downloads.Remove(this);
            }
        }
    }
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("EFEDC989-C396-41CA-83F7-07F845A55724")]
internal partial interface IDownloadStartingHandler
{
    void Invoke(nint sender, nint args);
}
