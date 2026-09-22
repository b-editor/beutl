using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Avalonia.Platform;

namespace Beutl.Editor.Components.WebBrowserTab;

// NativeWebView does not expose navigation responses. Inspect them before WebKit discards
// attachments, and forward all other delegate messages to Avalonia's navigation delegate.
[SupportedOSPlatform("macos")]
internal sealed unsafe partial class MacOSBrowserDownloadHandler : IDisposable
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";
    private static readonly Dictionary<nint, MacOSBrowserDownloadHandler> s_handlers = [];
    private static readonly nint s_responsePolicy = sel_registerName("webView:decidePolicyForNavigationResponse:decisionHandler:");
    private static readonly nint s_respondsToSelector = sel_registerName("respondsToSelector:");
    private static readonly nint s_delegateClass = CreateDelegateClass();
    private readonly Action<Uri, string> _onDownload;
    private readonly nint _originalDelegate;
    private nint _webView;
    private nint _delegate;

    private MacOSBrowserDownloadHandler(nint webView, Action<Uri, string> onDownload)
    {
        _webView = webView;
        _onDownload = onDownload;
        _originalDelegate = Send(Send(webView, sel_registerName("navigationDelegate")), sel_registerName("retain"));
        _delegate = Send(s_delegateClass, sel_registerName("new"));
        s_handlers.Add(_delegate, this);
        SendPointer(webView, sel_registerName("setNavigationDelegate:"), _delegate);
    }

    internal static MacOSBrowserDownloadHandler? TryAttach(IPlatformHandle? handle, Action<Uri, string> onDownload)
    {
        if (handle is not IAppleWKWebViewPlatformHandle apple) return null;
        nint webView = apple.GetWKWebViewRetained();
        return webView == 0 ? null : new MacOSBrowserDownloadHandler(webView, onDownload);
    }

    public void Dispose()
    {
        if (_webView == 0) return;
        // Keep the WebView alive until its original delegate has been restored.
        if (Send(_webView, sel_registerName("navigationDelegate")) == _delegate)
            SendPointer(_webView, sel_registerName("setNavigationDelegate:"), _originalDelegate);
        s_handlers.Remove(_delegate);
        SendVoid(_delegate, sel_registerName("release"));
        SendVoid(_originalDelegate, sel_registerName("release"));
        SendVoid(_webView, sel_registerName("release"));
        _delegate = 0;
        _webView = 0;
    }

    private static nint CreateDelegateClass()
    {
        nint type = objc_allocateClassPair(objc_getClass("NSObject"), "BeutlDownloadNavigationDelegate", 0);
        class_addMethod(type, s_responsePolicy,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>)&OnNavigationResponse, "v@:@@@");
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
    private static void OnNavigationResponse(nint self, nint selector, nint webView, nint navigationResponse, nint decisionHandler)
    {
        bool cancel = false;
        s_handlers.TryGetValue(self, out var handler);
        try
        {
            nint response = Send(navigationResponse, sel_registerName("response"));
            string? address = GetString(Send(Send(response, sel_registerName("URL")), sel_registerName("absoluteString")));
            if (handler != null && SendBool(navigationResponse, sel_registerName("isForMainFrame"))
                && Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) && BrowserMediaDownload.IsHttpUri(uri)
                && SendBoolPointer(response, s_respondsToSelector, sel_registerName("statusCode"))
                && Send(response, sel_registerName("statusCode")) is >= 200 and < 300
                && BrowserMediaDownload.GetResponseFileName(uri,
                    GetString(Send(response, sel_registerName("suggestedFilename"))),
                    GetString(Send(response, sel_registerName("MIMEType")))) is { } name)
            {
                handler._onDownload(uri, name);
                cancel = true;
            }
        }
        catch
        {
            // Exceptions must not escape a native delegate callback. Leave unrecognized responses to WebKit.
        }

        if (!cancel && handler != null && SendBoolPointer(handler._originalDelegate, s_respondsToSelector, selector))
        {
            SendResponsePolicy(handler._originalDelegate, selector, webView, navigationResponse, decisionHandler);
            return;
        }

        // Objective-C block ABI: isa, flags, reserved, invoke. Call the policy completion exactly once.
        var callback = (delegate* unmanaged[Cdecl]<nint, nint, void>)((BlockLiteral*)decisionHandler)->Invoke;
        callback(decisionHandler, cancel ? 0 : 1); // WKNavigationResponsePolicy.Cancel / Allow
    }

    private static string? GetString(nint value) =>
        value == 0 ? null : Marshal.PtrToStringUTF8(Send(value, sel_registerName("UTF8String")));

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
    private static partial void SendResponsePolicy(nint receiver, nint selector, nint webView, nint response, nint decisionHandler);
}
