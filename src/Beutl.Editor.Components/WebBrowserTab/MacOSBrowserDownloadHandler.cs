using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Avalonia.Platform;

using static Beutl.Editor.Components.Helpers.ObjCRuntime;

namespace Beutl.Editor.Components.WebBrowserTab;

// NativeWebView does not expose navigation responses. Inspect them before WebKit discards
// attachments, and forward all other delegate messages to Avalonia's navigation delegate.
// Download links to files a page script generated become WebKit downloads here as well.
[SupportedOSPlatform("macos")]
internal sealed partial class MacOSBrowserDownloadHandler : IDisposable
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";
    private static readonly Dictionary<nint, MacOSBrowserDownloadHandler> s_handlers = [];
    private static readonly nint s_responsePolicy = Selector("webView:decidePolicyForNavigationResponse:decisionHandler:");
    private static readonly nint s_respondsToSelector = Selector("respondsToSelector:");
    private static readonly nint s_delegateClass = CreateDelegateClass();
    private static readonly nint s_uiDelegateClass = CreateUIDelegateClass();
    private readonly Action<Uri, string, IBrowserDownloadSource> _onDownload;
    private readonly Action<Uri?, string, IBrowserDownloadSource, Uri?> _onGeneratedDownload;
    private readonly Action<Uri> _onNavigationCommitted;
    private readonly Action _onNavigationStarted;
    private readonly Action<Uri, string?> _onNewWindowRequested;
    private readonly Action<Uri> _onWindowOpened;
    private readonly Dictionary<nint, NativeDownload> _pendingDownloads = [];
    private readonly BrowserDownloadNavigation _navigation = new();
    private readonly BrowserNavigationCancellations _cancellations = new(
        static navigation => Send(navigation, Selector("retain")),
        static navigation => SendVoid(navigation, Selector("release")));
    private readonly nint _originalDelegate;
    private nint _webView;
    private nint _delegate;
    private nint _uiDelegate;

    private MacOSBrowserDownloadHandler(nint webView, Action<Uri, string, IBrowserDownloadSource> onDownload,
        Action<Uri?, string, IBrowserDownloadSource, Uri?> onGeneratedDownload, Action<Uri> onNavigationCommitted, Action onNavigationStarted,
        Action<Uri, string?> onNewWindowRequested, Action<Uri> onWindowOpened)
    {
        _webView = webView;
        _onDownload = onDownload;
        _onGeneratedDownload = onGeneratedDownload;
        _onNavigationCommitted = onNavigationCommitted;
        _onNavigationStarted = onNavigationStarted;
        _onNewWindowRequested = onNewWindowRequested;
        _onWindowOpened = onWindowOpened;
        _originalDelegate = Send(Send(webView, Selector("navigationDelegate")), Selector("retain"));
        _delegate = Send(s_delegateClass, Selector("new"));
        s_handlers.Add(_delegate, this);
        SendVoid(webView, Selector("setNavigationDelegate:"), _delegate);
        // Avalonia sets no UI delegate, which leaves a page's window.open() with nothing to open.
        if (Send(webView, Selector("UIDelegate")) == 0)
        {
            _uiDelegate = Send(s_uiDelegateClass, Selector("new"));
            s_handlers.Add(_uiDelegate, this);
            SendVoid(webView, Selector("setUIDelegate:"), _uiDelegate);
        }
    }

    internal static MacOSBrowserDownloadHandler? TryAttach(IPlatformHandle? handle, Action<Uri, string, IBrowserDownloadSource> onDownload,
        Action<Uri?, string, IBrowserDownloadSource, Uri?> onGeneratedDownload, Action<Uri> onNavigationCommitted, Action onNavigationStarted,
        Action<Uri, string?> onNewWindowRequested, Action<Uri> onWindowOpened)
    {
        if (handle is not IAppleWKWebViewPlatformHandle apple) return null;
        nint webView = apple.GetWKWebViewRetained();
        return webView == 0 ? null
            : new MacOSBrowserDownloadHandler(webView, onDownload, onGeneratedDownload, onNavigationCommitted, onNavigationStarted,
                onNewWindowRequested, onWindowOpened);
    }

    public void Dispose()
    {
        if (_webView == 0) return;
        // Keep the WebView alive until its original delegate has been restored.
        if (Send(_webView, Selector("navigationDelegate")) == _delegate)
            SendVoid(_webView, Selector("setNavigationDelegate:"), _originalDelegate);
        if (_uiDelegate != 0)
        {
            if (Send(_webView, Selector("UIDelegate")) == _uiDelegate)
                SendVoid(_webView, Selector("setUIDelegate:"), 0);
            s_handlers.Remove(_uiDelegate);
            SendVoid(_uiDelegate, Selector("release"));
            _uiDelegate = 0;
        }
        _cancellations.Dispose();
        foreach ((nint response, NativeDownload download) in _pendingDownloads)
        {
            download.Dispose();
            SendVoid(response, Selector("release"));
        }
        _pendingDownloads.Clear();
        s_handlers.Remove(_delegate);
        SendVoid(_delegate, Selector("release"));
        SendVoid(_originalDelegate, Selector("release"));
        SendVoid(_webView, Selector("release"));
        _delegate = 0;
        _webView = 0;
    }

    private static unsafe nint CreateDelegateClass()
    {
        nint type = objc_allocateClassPair(GetClass("NSObject"), "BeutlDownloadNavigationDelegate", 0);
        class_addMethod(type, s_responsePolicy,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>)&OnNavigationResponse, "v@:@@@");
        class_addMethod(type, Selector("webView:navigationResponse:didBecomeDownload:"),
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>)&OnResponseBecameDownload, "v@:@@@");
        class_addMethod(type, Selector("webView:decidePolicyForNavigationAction:decisionHandler:"),
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>)&OnNavigationAction, "v@:@@@");
        class_addMethod(type, Selector("webView:navigationAction:didBecomeDownload:"),
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>)&OnActionBecameDownload, "v@:@@@");
        class_addMethod(type, Selector("webView:didCommitNavigation:"),
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, void>)&OnNavigationCommitted, "v@:@@");
        class_addMethod(type, Selector("webView:didStartProvisionalNavigation:"),
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, void>)&OnNavigationStarted, "v@:@@");
        class_addMethod(type, Selector("webView:didFailProvisionalNavigation:withError:"),
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>)&OnNavigationFailed, "v@:@@@");
        class_addMethod(type, Selector("webView:didFailNavigation:withError:"),
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>)&OnNavigationFailed, "v@:@@@");
        class_addMethod(type, s_respondsToSelector,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, byte>)&RespondsToSelector, "c@::");
        class_addMethod(type, Selector("forwardingTargetForSelector:"),
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint>)&ForwardingTarget, "@@::");
        objc_registerClassPair(type);
        return type;
    }

    private static unsafe nint CreateUIDelegateClass()
    {
        nint type = objc_allocateClassPair(GetClass("NSObject"), "BeutlWindowOpenUIDelegate", 0);
        class_addMethod(type, Selector("webView:createWebViewWithConfiguration:forNavigationAction:windowFeatures:"),
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, nint, nint>)&OnCreateWebView, "@@:@@@@");
        objc_registerClassPair(type);
        return type;
    }

    // A page's window.open() asks the UI delegate for a web view, without asking the navigation delegate first. The
    // page opens in a new tab instead, with the referrer WebKit computed for the request as for a link with
    // target="_blank", and the page gets null back.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint OnCreateWebView(nint self, nint selector, nint webView, nint configuration, nint navigationAction,
        nint windowFeatures)
    {
        try
        {
            if (s_handlers.TryGetValue(self, out var handler))
            {
                nint request = Send(navigationAction, Selector("request"));
                string? address = ToManagedString(Send(Send(request, Selector("URL")), Selector("absoluteString")));
                if (Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) && BrowserMediaDownload.IsHttpUri(uri))
                {
                    handler._onNewWindowRequested(uri, GetHeader(request, "Referer"));
                    handler._onWindowOpened(uri);
                }
            }
        }
        catch
        {
            // Exceptions must not escape a native callback. The page then opens nothing, as before.
        }
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte RespondsToSelector(nint self, nint selector, nint requestedSelector) =>
        (byte)(class_respondsToSelector(s_delegateClass, requestedSelector)
            || (s_handlers.TryGetValue(self, out var handler)
                && SendBool(handler._originalDelegate, s_respondsToSelector, requestedSelector)) ? 1 : 0);

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
                nint request = Send(navigationAction, Selector("request"));
                if (IsGeneratedFileDownload(navigationAction, request))
                {
                    // Avalonia's delegate can only allow or cancel it, and Beutl cancels these schemes.
                    // Only this policy hands the page's file, from any frame, to a WebKit download.
                    CompletePolicy(decisionHandler, 2); // WKNavigationActionPolicy.Download
                    return;
                }
                nint targetFrame = Send(navigationAction, Selector("targetFrame"));
                bool isMainFrame = targetFrame != 0 && SendBool(targetFrame, Selector("isMainFrame"));
                if (targetFrame != 0 && !isMainFrame)
                {
                    // Avalonia's NavigationStarted omits frame identity. Keep embedded documents
                    // (including CAPTCHA srcdoc frames) out of the app's top-level URL policy.
                    CompletePolicy(decisionHandler, 1); // WKNavigationActionPolicy.Allow
                    return;
                }
                string? address = ToManagedString(Send(Send(request, Selector("URL")), Selector("absoluteString")));
                Uri.TryCreate(address, UriKind.Absolute, out Uri? uri);
                handler._navigation.RecordRequest(uri, ToManagedString(Send(request, Selector("HTTPMethod"))), isMainFrame);
                // A new window has no target frame. Avalonia raises NewWindowRequested from the policy decision this
                // forwards to, after the request names the referrer WebKit computed for it, or none.
                if (targetFrame == 0 && uri != null) handler._onNewWindowRequested(uri, GetHeader(request, "Referer"));
            }
            catch
            {
                // An unknown request must never inherit permission to replay an earlier GET.
                handler._navigation.RecordRequest(null, null, true);
            }
        }

        if (handler != null && SendBool(handler._originalDelegate, s_respondsToSelector, selector))
            SendNavigationPolicy(handler._originalDelegate, selector, webView, navigationAction, decisionHandler);
        else
            CompletePolicy(decisionHandler, 1); // WKNavigationActionPolicy.Allow
    }

    // A download link to a file the page generated. WKNavigationAction reports the download attribute since
    // macOS 11.3, the release that added the policy which turns the action into a download.
    private static bool IsGeneratedFileDownload(nint navigationAction, nint request)
    {
        nint shouldPerformDownload = Selector("shouldPerformDownload");
        return SendBool(navigationAction, s_respondsToSelector, shouldPerformDownload)
            && SendBool(navigationAction, shouldPerformDownload)
            && IsGeneratedFileRequest(request);
    }

    // Reads only the scheme, without copying a data: URL that holds the whole file.
    private static bool IsGeneratedFileRequest(nint request) => BrowserMediaDownload.IsGeneratedFileAddress(
        GetScheme(request) + ":");

    private static string? GetScheme(nint request) =>
        ToManagedString(Send(Send(request, Selector("URL")), Selector("scheme")));

    // A blob: URL names the origin that created it. A data: URL names no one, so the frame whose link
    // started the download is its creator.
    private static Uri? GetGeneratedFileCreator(nint navigationAction, nint request)
    {
        if (string.Equals(GetScheme(request), "blob", StringComparison.OrdinalIgnoreCase))
        {
            return BrowserMediaDownload.GetGeneratedFileOrigin(
                ToManagedString(Send(Send(request, Selector("URL")), Selector("absoluteString"))));
        }
        nint origin = Send(Send(navigationAction, Selector("sourceFrame")), Selector("securityOrigin"));
        string? protocol = ToManagedString(Send(origin, Selector("protocol")));
        string? host = ToManagedString(Send(origin, Selector("host")));
        nint port = Send(origin, Selector("port"));
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
                nint request = Send(navigationAction, Selector("request"));
                if (IsGeneratedFileRequest(request))
                {
                    Uri? creator = GetGeneratedFileCreator(navigationAction, request);
                    source = new NativeDownload((name, offered) => handler._onGeneratedDownload(creator, name, offered, null));
                    source.Attach(download);
                    return;
                }
            }
            catch
            {
                source?.Dispose();
            }
        }
        SendVoid(download, Selector("cancel:"), 0);
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
        if (SendBool(handler._originalDelegate, s_respondsToSelector, selector))
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
        if (SendBool(handler._originalDelegate, s_respondsToSelector, selector))
            SendNavigationFailure(handler._originalDelegate, selector, webView, navigation, error);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnNavigationCommitted(nint self, nint selector, nint webView, nint navigation)
    {
        if (!s_handlers.TryGetValue(self, out var handler)) return;
        try
        {
            handler._cancellations.CommitNavigation(navigation);
            string? address = ToManagedString(Send(Send(webView, Selector("URL")), Selector("absoluteString")));
            if (Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)) handler._onNavigationCommitted(uri);
        }
        catch
        {
            // Still forward the native notification if recording the committed document fails.
        }
        if (SendBool(handler._originalDelegate, s_respondsToSelector, selector))
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
            nint response = Send(navigationResponse, Selector("response"));
            string? address = ToManagedString(Send(Send(response, Selector("URL")), Selector("absoluteString")));
            string? mediaType = ToManagedString(Send(response, Selector("MIMEType")));
            if (handler != null && SendBool(navigationResponse, Selector("isForMainFrame"))
                && handler._cancellations.Current != 0
                && Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)
                && SendBool(response, s_respondsToSelector, Selector("statusCode"))
                && handler._navigation.IsCompleteGetResponse(uri, Send(response, Selector("statusCode")))
                && BrowserMediaDownload.IsDownloadResponse(mediaType,
                    SendBool(navigationResponse, Selector("canShowMIMEType")), GetHeader(response, "Content-Disposition"))
                && BrowserMediaDownload.GetResponseFileName(uri,
                    ToManagedString(Send(response, Selector("suggestedFilename"))), mediaType) is { } name)
            {
                nint navigation = handler._cancellations.Current;
                if (handler._cancellations.MarkCanceled(navigation)) canceledNavigation = navigation;
                source = new NativeDownload(name, ToManagedString(Send(response, Selector("textEncodingName"))));
                // WebKit retains this same response for didBecomeDownload. Keep its wrapper alive
                // so simultaneous conversions are matched by object identity, never by URL.
                handler._pendingDownloads.Add(Send(navigationResponse, Selector("retain")), source);
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

        if (policy == 1 && handler != null && SendBool(handler._originalDelegate, s_respondsToSelector, selector))
        {
            SendNavigationPolicy(handler._originalDelegate, selector, webView, navigationResponse, decisionHandler);
            return;
        }

        CompletePolicy(decisionHandler, policy);
    }

    private NativeDownload? RemovePendingDownload(nint response)
    {
        if (!_pendingDownloads.Remove(response, out var source)) return null;
        SendVoid(response, Selector("release"));
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
                    SendVoid(download, Selector("cancel:"), 0);
                }
                return;
            }
            if (SendBool(handler._originalDelegate, s_respondsToSelector, selector))
            {
                SendNavigationDownload(handler._originalDelegate, selector, webView, response, download);
                return;
            }
        }
        SendVoid(download, Selector("cancel:"), 0);
    }

    private static unsafe void CompletePolicy(nint decisionHandler, nint policy)
    {
        // Objective-C block ABI: isa, flags, reserved, invoke. Call the policy completion exactly once.
        var callback = (delegate* unmanaged[Cdecl]<nint, nint, void>)((BlockLiteral*)decisionHandler)->Invoke;
        callback(decisionHandler, policy);
    }

    // NSURLRequest and NSHTTPURLResponse match header names case-insensitively.
    private static string? GetHeader(nint message, string name) =>
        ToManagedString(Send(message, Selector("valueForHTTPHeaderField:"), AutoreleasedString(name)));

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockLiteral
    {
        public nint Isa;
        public int Flags;
        public int Reserved;
        public nint Invoke;
    }

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
    private static partial void SendNavigationPolicy(nint receiver, nint selector, nint webView, nint navigation, nint decisionHandler);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendNavigation(nint receiver, nint selector, nint webView, nint navigation);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendNavigationFailure(nint receiver, nint selector, nint webView, nint navigation, nint error);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendNavigationDownload(nint receiver, nint selector, nint webView, nint response, nint download);
}
