using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Avalonia.Platform;

namespace Beutl.Editor.Components.WebBrowserTab;

// NativeWebView does not expose navigation responses. Inspect them before WebKit discards
// attachments, and forward all other delegate messages to Avalonia's navigation delegate.
// Download links to files a page script generated become WebKit downloads here as well.
[SupportedOSPlatform("macos")]
internal sealed partial class MacOSBrowserDownloadHandler : IDisposable
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";
    private static readonly Dictionary<nint, MacOSBrowserDownloadHandler> s_handlers = [];
    private static readonly nint s_responsePolicy = sel_registerName("webView:decidePolicyForNavigationResponse:decisionHandler:");
    private static readonly nint s_respondsToSelector = sel_registerName("respondsToSelector:");
    private static readonly nint s_delegateClass = CreateDelegateClass();
    private readonly Action<Uri, string, IBrowserDownloadSource> _onDownload;
    private readonly Action<Uri?, string, IBrowserDownloadSource> _onGeneratedDownload;
    private readonly Action<Uri> _onNavigationCommitted;
    private readonly Action _onNavigationStarted;
    private readonly Dictionary<nint, NativeDownload> _pendingDownloads = [];
    private readonly BrowserDownloadNavigation _navigation = new();
    private readonly BrowserNavigationCancellations _cancellations = new(
        static navigation => Send(navigation, sel_registerName("retain")),
        static navigation => SendVoid(navigation, sel_registerName("release")));
    private readonly nint _originalDelegate;
    private nint _webView;
    private nint _delegate;

    private MacOSBrowserDownloadHandler(nint webView, Action<Uri, string, IBrowserDownloadSource> onDownload,
        Action<Uri?, string, IBrowserDownloadSource> onGeneratedDownload, Action<Uri> onNavigationCommitted, Action onNavigationStarted)
    {
        _webView = webView;
        _onDownload = onDownload;
        _onGeneratedDownload = onGeneratedDownload;
        _onNavigationCommitted = onNavigationCommitted;
        _onNavigationStarted = onNavigationStarted;
        _originalDelegate = Send(Send(webView, sel_registerName("navigationDelegate")), sel_registerName("retain"));
        _delegate = Send(s_delegateClass, sel_registerName("new"));
        s_handlers.Add(_delegate, this);
        SendPointer(webView, sel_registerName("setNavigationDelegate:"), _delegate);
    }

    internal static MacOSBrowserDownloadHandler? TryAttach(IPlatformHandle? handle, Action<Uri, string, IBrowserDownloadSource> onDownload,
        Action<Uri?, string, IBrowserDownloadSource> onGeneratedDownload, Action<Uri> onNavigationCommitted, Action onNavigationStarted)
    {
        if (handle is not IAppleWKWebViewPlatformHandle apple) return null;
        nint webView = apple.GetWKWebViewRetained();
        return webView == 0 ? null
            : new MacOSBrowserDownloadHandler(webView, onDownload, onGeneratedDownload, onNavigationCommitted, onNavigationStarted);
    }

    public void Dispose()
    {
        if (_webView == 0) return;
        // Keep the WebView alive until its original delegate has been restored.
        if (Send(_webView, sel_registerName("navigationDelegate")) == _delegate)
            SendPointer(_webView, sel_registerName("setNavigationDelegate:"), _originalDelegate);
        _cancellations.Dispose();
        foreach ((nint response, NativeDownload download) in _pendingDownloads)
        {
            download.Dispose();
            SendVoid(response, sel_registerName("release"));
        }
        _pendingDownloads.Clear();
        s_handlers.Remove(_delegate);
        SendVoid(_delegate, sel_registerName("release"));
        SendVoid(_originalDelegate, sel_registerName("release"));
        SendVoid(_webView, sel_registerName("release"));
        _delegate = 0;
        _webView = 0;
    }

    private static unsafe nint CreateDelegateClass()
    {
        nint type = objc_allocateClassPair(objc_getClass("NSObject"), "BeutlDownloadNavigationDelegate", 0);
        class_addMethod(type, s_responsePolicy,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>)&OnNavigationResponse, "v@:@@@");
        class_addMethod(type, sel_registerName("webView:navigationResponse:didBecomeDownload:"),
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>)&OnResponseBecameDownload, "v@:@@@");
        class_addMethod(type, sel_registerName("webView:decidePolicyForNavigationAction:decisionHandler:"),
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>)&OnNavigationAction, "v@:@@@");
        class_addMethod(type, sel_registerName("webView:navigationAction:didBecomeDownload:"),
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>)&OnActionBecameDownload, "v@:@@@");
        class_addMethod(type, sel_registerName("webView:didCommitNavigation:"),
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, void>)&OnNavigationCommitted, "v@:@@");
        class_addMethod(type, sel_registerName("webView:didStartProvisionalNavigation:"),
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, void>)&OnNavigationStarted, "v@:@@");
        class_addMethod(type, sel_registerName("webView:didFailProvisionalNavigation:withError:"),
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>)&OnNavigationFailed, "v@:@@@");
        class_addMethod(type, sel_registerName("webView:didFailNavigation:withError:"),
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>)&OnNavigationFailed, "v@:@@@");
        class_addMethod(type, s_respondsToSelector,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, byte>)&RespondsToSelector, "c@::");
        class_addMethod(type, sel_registerName("forwardingTargetForSelector:"),
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint>)&ForwardingTarget, "@@::");
        objc_registerClassPair(type);
        return type;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte RespondsToSelector(nint self, nint selector, nint requestedSelector) =>
        (byte)(class_respondsToSelector(s_delegateClass, requestedSelector)
            || (s_handlers.TryGetValue(self, out var handler)
                && SendBoolPointer(handler._originalDelegate, s_respondsToSelector, requestedSelector)) ? 1 : 0);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint ForwardingTarget(nint self, nint selector, nint requestedSelector) =>
        s_handlers.TryGetValue(self, out var handler) ? handler._originalDelegate : 0;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnNavigationAction(nint self, nint selector, nint webView, nint navigationAction, nint decisionHandler)
    {
        s_handlers.TryGetValue(self, out var handler);
        if (handler != null)
        {
            try
            {
                nint request = Send(navigationAction, sel_registerName("request"));
                if (IsGeneratedFileDownload(navigationAction, request))
                {
                    // Avalonia's delegate can only allow or cancel it, and Beutl cancels these schemes.
                    // Only this policy hands the page's file, from any frame, to a WebKit download.
                    CompletePolicy(decisionHandler, 2); // WKNavigationActionPolicy.Download
                    return;
                }
                nint targetFrame = Send(navigationAction, sel_registerName("targetFrame"));
                bool isMainFrame = targetFrame != 0 && SendBool(targetFrame, sel_registerName("isMainFrame"));
                if (targetFrame != 0 && !isMainFrame)
                {
                    // Avalonia's NavigationStarted omits frame identity. Keep embedded documents
                    // (including CAPTCHA srcdoc frames) out of the app's top-level URL policy.
                    CompletePolicy(decisionHandler, 1); // WKNavigationActionPolicy.Allow
                    return;
                }
                string? address = GetString(Send(Send(request, sel_registerName("URL")), sel_registerName("absoluteString")));
                Uri.TryCreate(address, UriKind.Absolute, out Uri? uri);
                handler._navigation.RecordRequest(uri, GetString(Send(request, sel_registerName("HTTPMethod"))), isMainFrame);
            }
            catch
            {
                // An unknown request must never inherit permission to replay an earlier GET.
                handler._navigation.RecordRequest(null, null, true);
            }
        }

        if (handler != null && SendBoolPointer(handler._originalDelegate, s_respondsToSelector, selector))
            SendNavigationPolicy(handler._originalDelegate, selector, webView, navigationAction, decisionHandler);
        else
            CompletePolicy(decisionHandler, 1); // WKNavigationActionPolicy.Allow
    }

    // A download link to a file the page generated. WKNavigationAction reports the download attribute since
    // macOS 11.3, the release that added the policy which turns the action into a download.
    private static bool IsGeneratedFileDownload(nint navigationAction, nint request)
    {
        nint shouldPerformDownload = sel_registerName("shouldPerformDownload");
        return SendBoolPointer(navigationAction, s_respondsToSelector, shouldPerformDownload)
            && SendBool(navigationAction, shouldPerformDownload)
            && IsGeneratedFileRequest(request);
    }

    // Reads only the scheme, without copying a data: URL that holds the whole file.
    private static bool IsGeneratedFileRequest(nint request) => BrowserMediaDownload.IsGeneratedFileAddress(
        GetScheme(request) + ":");

    private static string? GetScheme(nint request) =>
        GetString(Send(Send(request, sel_registerName("URL")), sel_registerName("scheme")));

    // A blob: URL names the origin that created it. A data: URL names no one, so the frame whose link
    // started the download is its creator.
    private static Uri? GetGeneratedFileCreator(nint navigationAction, nint request)
    {
        if (string.Equals(GetScheme(request), "blob", StringComparison.OrdinalIgnoreCase))
        {
            return BrowserMediaDownload.GetGeneratedFileOrigin(
                GetString(Send(Send(request, sel_registerName("URL")), sel_registerName("absoluteString"))));
        }
        nint origin = Send(Send(navigationAction, sel_registerName("sourceFrame")), sel_registerName("securityOrigin"));
        string? protocol = GetString(Send(origin, sel_registerName("protocol")));
        string? host = GetString(Send(origin, sel_registerName("host")));
        nint port = Send(origin, sel_registerName("port"));
        return protocol is "http" or "https" && !string.IsNullOrEmpty(host)
            ? new UriBuilder(protocol, host, port == 0 ? -1 : (int)port).Uri : null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnActionBecameDownload(nint self, nint selector, nint webView, nint navigationAction, nint download)
    {
        if (s_handlers.TryGetValue(self, out var handler))
        {
            NativeDownload? source = null;
            try
            {
                // Only generated files are given the download policy. WebKit names the file when it asks
                // for a destination, so the file is offered then.
                nint request = Send(navigationAction, sel_registerName("request"));
                if (IsGeneratedFileRequest(request))
                {
                    Uri? creator = GetGeneratedFileCreator(navigationAction, request);
                    source = new NativeDownload((name, offered) => handler._onGeneratedDownload(creator, name, offered));
                    source.Attach(download);
                    return;
                }
            }
            catch
            {
                source?.Dispose();
            }
        }
        SendPointer(download, sel_registerName("cancel:"), 0);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnNavigationStarted(nint self, nint selector, nint webView, nint navigation)
    {
        if (!s_handlers.TryGetValue(self, out var handler)) return;
        try
        {
            handler._cancellations.BeginNavigation(navigation);
            handler._onNavigationStarted();
        }
        catch
        {
            // Leave native notifications with Avalonia if tracking cannot be initialized.
        }
        if (SendBoolPointer(handler._originalDelegate, s_respondsToSelector, selector))
            SendNavigation(handler._originalDelegate, selector, webView, navigation);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnNavigationFailed(nint self, nint selector, nint webView, nint navigation, nint error)
    {
        if (!s_handlers.TryGetValue(self, out var handler)) return;
        try
        {
            // Consume our cancellation before Avalonia drops the WKNavigation identity.
            if (handler._cancellations.CompleteNavigation(navigation)) return;
        }
        catch
        {
            // Unrecognized failures still belong to Avalonia's delegate.
        }
        if (SendBoolPointer(handler._originalDelegate, s_respondsToSelector, selector))
            SendNavigationFailure(handler._originalDelegate, selector, webView, navigation, error);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnNavigationCommitted(nint self, nint selector, nint webView, nint navigation)
    {
        if (!s_handlers.TryGetValue(self, out var handler)) return;
        try
        {
            handler._cancellations.CommitNavigation(navigation);
            string? address = GetString(Send(Send(webView, sel_registerName("URL")), sel_registerName("absoluteString")));
            if (Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)) handler._onNavigationCommitted(uri);
        }
        catch
        {
            // Still forward the native notification if recording the committed document fails.
        }
        if (SendBoolPointer(handler._originalDelegate, s_respondsToSelector, selector))
            SendNavigation(handler._originalDelegate, selector, webView, navigation);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnNavigationResponse(nint self, nint selector, nint webView, nint navigationResponse, nint decisionHandler)
    {
        nint policy = 1; // WKNavigationResponsePolicy.Allow
        nint canceledNavigation = 0;
        NativeDownload? source = null;
        s_handlers.TryGetValue(self, out var handler);
        try
        {
            nint response = Send(navigationResponse, sel_registerName("response"));
            string? address = GetString(Send(Send(response, sel_registerName("URL")), sel_registerName("absoluteString")));
            string? mediaType = GetString(Send(response, sel_registerName("MIMEType")));
            if (handler != null && SendBool(navigationResponse, sel_registerName("isForMainFrame"))
                && handler._cancellations.Current != 0
                && Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)
                && SendBoolPointer(response, s_respondsToSelector, sel_registerName("statusCode"))
                && handler._navigation.IsCompleteGetResponse(uri, Send(response, sel_registerName("statusCode")))
                && BrowserMediaDownload.IsDownloadResponse(mediaType,
                    SendBool(navigationResponse, sel_registerName("canShowMIMEType")), GetHeader(response, "Content-Disposition"))
                && BrowserMediaDownload.GetResponseFileName(uri,
                    GetString(Send(response, sel_registerName("suggestedFilename"))), mediaType) is { } name)
            {
                nint navigation = handler._cancellations.Current;
                if (handler._cancellations.MarkCanceled(navigation)) canceledNavigation = navigation;
                source = new NativeDownload(name, GetString(Send(response, sel_registerName("textEncodingName"))));
                // WebKit retains this same response for didBecomeDownload. Keep its wrapper alive
                // so simultaneous conversions are matched by object identity, never by URL.
                handler._pendingDownloads.Add(Send(navigationResponse, sel_registerName("retain")), source);
                handler._onDownload(uri, name, source);
                policy = source.IsDisposed ? 0 : 2; // Cancel or transfer the existing response to WKDownload.
                if (source.IsDisposed) handler.RemovePendingDownload(navigationResponse);
            }
        }
        catch
        {
            // Exceptions must not escape a native delegate callback. Leave unrecognized responses to WebKit.
            source?.Dispose();
            handler?.RemovePendingDownload(navigationResponse);
            if (canceledNavigation != 0) handler?._cancellations.ForgetCancellation(canceledNavigation);
        }

        if (policy == 1 && handler != null && SendBoolPointer(handler._originalDelegate, s_respondsToSelector, selector))
        {
            SendNavigationPolicy(handler._originalDelegate, selector, webView, navigationResponse, decisionHandler);
            return;
        }

        CompletePolicy(decisionHandler, policy);
    }

    private NativeDownload? RemovePendingDownload(nint response)
    {
        if (!_pendingDownloads.Remove(response, out var source)) return null;
        SendVoid(response, sel_registerName("release"));
        return source;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnResponseBecameDownload(nint self, nint selector, nint webView, nint response, nint download)
    {
        if (s_handlers.TryGetValue(self, out var handler))
        {
            NativeDownload? source = handler.RemovePendingDownload(response);
            if (source != null)
            {
                try { source.Attach(download); }
                catch
                {
                    source.Dispose();
                    SendPointer(download, sel_registerName("cancel:"), 0);
                }
                return;
            }
            if (SendBoolPointer(handler._originalDelegate, s_respondsToSelector, selector))
            {
                SendNavigationDownload(handler._originalDelegate, selector, webView, response, download);
                return;
            }
        }
        SendPointer(download, sel_registerName("cancel:"), 0);
    }

    private static unsafe void CompletePolicy(nint decisionHandler, nint policy)
    {
        // Objective-C block ABI: isa, flags, reserved, invoke. Call the policy completion exactly once.
        var callback = (delegate* unmanaged[Cdecl]<nint, nint, void>)((BlockLiteral*)decisionHandler)->Invoke;
        callback(decisionHandler, policy);
    }

    private static string? GetString(nint value) =>
        value == 0 ? null : Marshal.PtrToStringUTF8(Send(value, sel_registerName("UTF8String")));

    // NSHTTPURLResponse matches header names case-insensitively.
    private static string? GetHeader(nint response, string name) => GetString(SendArgument(response,
        sel_registerName("valueForHTTPHeaderField:"), SendString(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), name)));

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockLiteral
    {
        public nint Isa;
        public int Flags;
        public int Reserved;
        public nint Invoke;
    }

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_getClass(string name);

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_allocateClassPair(nint superclass, string name, nuint extraBytes);

    [LibraryImport(LibObjC)]
    private static partial void objc_registerClassPair(nint type);

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool class_addMethod(nint type, nint selector, nint implementation, string signature);

    [LibraryImport(LibObjC)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool class_respondsToSelector(nint type, nint selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendVoid(nint receiver, nint selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendPointer(nint receiver, nint selector, nint value);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool SendBool(nint receiver, nint selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool SendBoolPointer(nint receiver, nint selector, nint value);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendNavigationPolicy(nint receiver, nint selector, nint webView, nint navigation, nint decisionHandler);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendNavigation(nint receiver, nint selector, nint webView, nint navigation);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendNavigationFailure(nint receiver, nint selector, nint webView, nint navigation, nint error);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendNavigationDownload(nint receiver, nint selector, nint webView, nint response, nint download);
}
