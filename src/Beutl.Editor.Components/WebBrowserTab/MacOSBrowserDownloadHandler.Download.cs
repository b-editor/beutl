using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Avalonia.Threading;

using static Beutl.Editor.Components.Helpers.ObjCRuntime;

namespace Beutl.Editor.Components.WebBrowserTab;

internal sealed partial class MacOSBrowserDownloadHandler
{
    private sealed class NativeDownload : IBrowserDownloadSource
    {
        private static readonly Dictionary<nint, NativeDownload> s_downloads = [];
        private static readonly nint s_downloadDelegateClass = CreateDownloadDelegateClass();
        private readonly TaskCompletionSource _destinationReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _lifetime = new();
        private readonly Action<string, IBrowserDownloadSource>? _offerGenerated;
        private readonly string? _charset;
        private string? _fileName;
        private nint _download;
        private nint _delegate;
        private nint _destinationHandler;
        private DispatcherTimer? _progressTimer;
        private bool _saving;

        // A response, named from its headers before WebKit turns it into a download.
        internal NativeDownload(string fileName, string? charset)
        {
            _fileName = fileName;
            _charset = charset;
        }

        // A file the page generated, which WebKit names only when it asks where to save it. It is offered then.
        internal NativeDownload(Action<string, IBrowserDownloadSource> offerGenerated) => _offerGenerated = offerGenerated;

        internal bool IsDisposed { get; private set; }

        public bool IsGenerated => _offerGenerated != null;

        internal void Attach(nint download)
        {
            if (IsDisposed)
            {
                SendVoid(download, Selector("cancel:"), 0);
                return;
            }
            _download = Send(download, Selector("retain"));
            _delegate = Send(s_downloadDelegateClass, Selector("new"));
            s_downloads.Add(_delegate, this);
            SendVoid(download, Selector("setDelegate:"), _delegate);
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
                (stagingDirectory, string temporaryPath) = BrowserMediaDownload.CreateStagingPaths(directory);
                Directory.CreateDirectory(stagingDirectory);
                _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _progressTimer.Tick += (_, _) =>
                {
                    if (IsDisposed || _download == 0) return;
                    try
                    {
                        nint nativeProgress = Send(_download, Selector("progress"));
                        long received = Send(nativeProgress, Selector("completedUnitCount"));
                        long total = Send(nativeProgress, Selector("totalUnitCount"));
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
                return await BrowserMediaDownload.PublishNativeFileAsync(temporaryPath, directory, _fileName!, IsGenerated, _charset,
                    cancellation.Token);
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
                SendVoid(_download, Selector("setDelegate:"), 0);
                if (!_finished.Task.IsCompletedSuccessfully) SendVoid(_download, Selector("cancel:"), 0);
                SendVoid(_delegate, Selector("release"));
                SendVoid(_download, Selector("release"));
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
            nint type = objc_allocateClassPair(GetClass("NSObject"), "BeutlDownloadDelegate", 0);
            class_addMethod(type, Selector("download:decideDestinationUsingResponse:suggestedFilename:completionHandler:"),
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, nint, void>)&OnDestination, "v@:@@@@");
            class_addMethod(type, Selector("downloadDidFinish:"),
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnFinished, "v@:@");
            class_addMethod(type, Selector("download:didFailWithError:resumeData:"),
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
                if (source.IsGenerated)
                {
                    source._fileName = BrowserMediaDownload.CreateGeneratedFileName(ToManagedString(name),
                        ToManagedString(Send(response, Selector("MIMEType"))));
                }
                source._destinationHandler = BlockCopy(completion);
                if (source._destinationHandler == 0) throw new IOException(Strings.WebDownloadIncomplete);
                source._destinationReady.TrySetResult();
            }
            catch (Exception ex)
            {
                CompleteDestination(completion, null);
                source._destinationReady.TrySetException(ex);
                // A generated file not yet offered has no one else to release it.
                if (source.IsGenerated) Dispatcher.UIThread.Post(source.Dispose);
                return;
            }
            if (source._offerGenerated is not { } offer) return;
            // Disposing replaces the download's delegate, which must not happen inside its own callback,
            // and the view may dispose the offered source at once.
            Dispatcher.UIThread.Post(() =>
            {
                if (source.IsDisposed) return;
                try { offer(source._fileName!, source); }
                catch { source.Dispose(); }
            });
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
            try { message = ToManagedString(Send(error, Selector("localizedDescription"))) ?? Strings.WebDownloadIncomplete; }
            catch { message = Strings.WebDownloadIncomplete; }
            var exception = new IOException(message);
            source._destinationReady.TrySetException(exception);
            source._finished.TrySetException(exception);
            // A generated file that failed before WebKit named it was never offered, so no one else releases it.
            if (source.IsGenerated && source._fileName == null) Dispatcher.UIThread.Post(source.Dispose);
        }
    }

    private static unsafe void CompleteDestination(nint block, string? path)
    {
        nint url = 0;
        if (path != null)
        {
            nint text = AutoreleasedString(path);
            url = Send(GetClass("NSURL"), Selector("fileURLWithPath:"), text);
        }
        var callback = (delegate* unmanaged[Cdecl]<nint, nint, void>)((BlockLiteral*)block)->Invoke;
        callback(block, url);
    }

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_Block_copy")]
    private static partial nint BlockCopy(nint block);

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_Block_release")]
    private static partial void BlockRelease(nint block);
}
