using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Avalonia.Threading;

namespace Beutl.Editor.Components.WebBrowserTab;

internal sealed partial class MacOSBrowserDownloadHandler
{
    private sealed class NativeDownload(string fileName, string? charset) : IBrowserDownloadSource
    {
        private static readonly Dictionary<nint, NativeDownload> s_downloads = [];
        private static readonly nint s_downloadDelegateClass = CreateDownloadDelegateClass();
        private readonly TaskCompletionSource _destinationReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _lifetime = new();
        private nint _download;
        private nint _delegate;
        private nint _destinationHandler;
        private DispatcherTimer? _progressTimer;
        private bool _saving;

        internal bool IsDisposed { get; private set; }

        internal void Attach(nint download)
        {
            if (IsDisposed)
            {
                SendPointer(download, sel_registerName("cancel:"), 0);
                return;
            }
            _download = Send(download, sel_registerName("retain"));
            _delegate = Send(s_downloadDelegateClass, sel_registerName("new"));
            s_downloads.Add(_delegate, this);
            SendPointer(download, sel_registerName("setDelegate:"), _delegate);
        }

        public async Task<string> SaveAsync(string directory, IProgress<(long Received, long? Total)>? progress,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_saving) throw new InvalidOperationException("The native response can only be saved once.");
            _saving = true;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            using var registration = cancellation.Token.Register(() =>
            {
                if (Dispatcher.UIThread.CheckAccess()) Dispose();
                else Dispatcher.UIThread.Post(Dispose);
            });
            string? stagingDirectory = null;
            try
            {
                await _destinationReady.Task.WaitAsync(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                stagingDirectory = Path.Combine(Path.GetFullPath(directory), $".beutl-download-{Guid.NewGuid():N}");
                Directory.CreateDirectory(stagingDirectory);
                string temporaryPath = Path.Combine(stagingDirectory, "response");
                _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _progressTimer.Tick += (_, _) =>
                {
                    if (IsDisposed || _download == 0) return;
                    try
                    {
                        nint nativeProgress = Send(_download, sel_registerName("progress"));
                        long received = Send(nativeProgress, sel_registerName("completedUnitCount"));
                        long total = Send(nativeProgress, sel_registerName("totalUnitCount"));
                        progress?.Report((Math.Max(0, received), total > 0 ? total : null));
                    }
                    catch (Exception ex)
                    {
                        _finished.TrySetException(ex);
                    }
                };
                _progressTimer.Start();
                ChooseDestination(temporaryPath);
                await _finished.Task.WaitAsync(cancellation.Token);
                _progressTimer.Stop();
                await using var input = new FileStream(temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    81920, FileOptions.Asynchronous);
                return await BrowserMediaDownload.SaveAsync(input, directory, fileName, null, cancellation.Token, input.Length, charset);
            }
            finally
            {
                _progressTimer?.Stop();
                if (stagingDirectory != null && Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, true);
            }
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            _lifetime.Cancel();
            _destinationReady.TrySetCanceled();
            _finished.TrySetCanceled();
            if (_destinationReady.Task.IsFaulted) _ = _destinationReady.Task.Exception;
            if (_finished.Task.IsFaulted) _ = _finished.Task.Exception;
            _progressTimer?.Stop();
            ChooseDestination(null);
            if (_download != 0)
            {
                s_downloads.Remove(_delegate);
                SendPointer(_download, sel_registerName("setDelegate:"), 0);
                if (!_finished.Task.IsCompletedSuccessfully) SendPointer(_download, sel_registerName("cancel:"), 0);
                SendVoid(_delegate, sel_registerName("release"));
                SendVoid(_download, sel_registerName("release"));
                _delegate = 0;
                _download = 0;
            }
            _lifetime.Dispose();
        }

        private void ChooseDestination(string? path)
        {
            nint block = _destinationHandler;
            _destinationHandler = 0;
            if (block == 0) return;
            try { CompleteDestination(block, path); }
            finally { BlockRelease(block); }
        }

        private static unsafe nint CreateDownloadDelegateClass()
        {
            nint type = objc_allocateClassPair(objc_getClass("NSObject"), "BeutlDownloadDelegate", 0);
            class_addMethod(type, sel_registerName("download:decideDestinationUsingResponse:suggestedFilename:completionHandler:"),
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, nint, void>)&OnDestination, "v@:@@@@");
            class_addMethod(type, sel_registerName("downloadDidFinish:"),
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnFinished, "v@:@");
            class_addMethod(type, sel_registerName("download:didFailWithError:resumeData:"),
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>)&OnFailed, "v@:@@@");
            objc_registerClassPair(type);
            return type;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnDestination(nint self, nint selector, nint download, nint response, nint name, nint completion)
        {
            if (!s_downloads.TryGetValue(self, out var source) || source.IsDisposed || source._destinationReady.Task.IsCompleted)
            {
                CompleteDestination(completion, null);
                return;
            }
            try
            {
                source._destinationHandler = BlockCopy(completion);
                if (source._destinationHandler == 0) throw new IOException(Strings.WebDownloadIncomplete);
                source._destinationReady.TrySetResult();
            }
            catch (Exception ex)
            {
                CompleteDestination(completion, null);
                source._destinationReady.TrySetException(ex);
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnFinished(nint self, nint selector, nint download)
        {
            if (s_downloads.TryGetValue(self, out var source)) source._finished.TrySetResult();
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnFailed(nint self, nint selector, nint download, nint error, nint resumeData)
        {
            if (!s_downloads.TryGetValue(self, out var source)) return;
            string message;
            try { message = GetString(Send(error, sel_registerName("localizedDescription"))) ?? Strings.WebDownloadIncomplete; }
            catch { message = Strings.WebDownloadIncomplete; }
            var exception = new IOException(message);
            source._destinationReady.TrySetException(exception);
            source._finished.TrySetException(exception);
        }
    }

    private static unsafe void CompleteDestination(nint block, string? path)
    {
        nint url = 0;
        if (path != null)
        {
            nint text = SendString(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), path);
            url = SendArgument(objc_getClass("NSURL"), sel_registerName("fileURLWithPath:"), text);
        }
        var callback = (delegate* unmanaged[Cdecl]<nint, nint, void>)((BlockLiteral*)block)->Invoke;
        callback(block, url);
    }

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_Block_copy")]
    private static partial nint BlockCopy(nint block);

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_Block_release")]
    private static partial void BlockRelease(nint block);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint SendString(nint receiver, nint selector, string value);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendArgument(nint receiver, nint selector, nint value);
}
