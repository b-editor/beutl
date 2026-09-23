using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Avalonia.Platform;

namespace Beutl.Editor.Components.WebBrowserTab;

// NativeWebView exposes an owned ICoreWebView2 COM reference, but its managed
// WebResourceRequested event cannot supply a response. Use the published COM ABI.
internal sealed unsafe partial class WindowsAdBlockBackend : IBrowserAdBlockBackend
{
    private nint _webView;
    private nint _response;
    private long _token;
    private bool _subscribed;
    private readonly RequestHandler _handler;
    // ICoreWebView2 vtable positions, including IUnknown's three methods.
    private const int AddRequested = 55, RemoveRequested = 56, AddFilter = 57, RemoveFilter = 58;

    internal WindowsAdBlockBackend(IWindowsWebView2PlatformHandle handle, BrowserAdBlockRules rules, Func<Uri> getPage)
    {
        _webView = handle.CoreWebView2;
        if (_webView == 0) throw new InvalidOperationException("WebView2 is not available.");
        _handler = new RequestHandler(this, rules, getPage);
        nint extended = 0, environment = 0;
        try
        {
            var iid = new Guid("9E8F0CF8-E670-4B5E-B2BC-73E061E3184C");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(_webView, in iid, out extended));
            // ICoreWebView2_2::get_Environment is slot 67.
            environment = GetObject(extended, 67);
            fixed (char* reason = "Blocked by Beutl")
            fixed (char* headers = "Content-Type: text/plain\r\nCache-Control: no-store\r\n")
            {
                nint response;
                var create = (delegate* unmanaged[Stdcall]<nint, nint, int, char*, char*, nint*, int>)Method(environment, 4);
                Marshal.ThrowExceptionForHR(create(environment, 0, 403, reason, headers, &response));
                _response = response;
            }
        }
        catch { Dispose(); throw; }
        finally
        {
            if (environment != 0) Marshal.Release(environment);
            if (extended != 0) Marshal.Release(extended);
        }
    }

    public Task EnableAsync()
    {
        if (_webView == 0 || _subscribed) return Task.CompletedTask;
        void* handler = ComInterfaceMarshaller<IAdBlockRequestHandler>.ConvertToUnmanaged(_handler);
        try
        {
            long token;
            var subscribe = (delegate* unmanaged[Stdcall]<nint, void*, long*, int>)Method(_webView, AddRequested);
            Marshal.ThrowExceptionForHR(subscribe(_webView, handler, &token));
            _token = token;
            _subscribed = true;
            // Separate HTTP filters avoid owning/removing Avalonia's '*' observer filter.
            SetFilter(AddFilter, "http://*/*");
            SetFilter(AddFilter, "https://*/*");
        }
        catch { Dispose(); throw; }
        finally { ComInterfaceMarshaller<IAdBlockRequestHandler>.Free(handler); }
        return Task.CompletedTask;
    }

    private void SetFilter(int slot, string pattern)
    {
        fixed (char* uri = pattern)
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, char*, int, int>)Method(_webView, slot))(_webView, uri, 0));
    }

    public void Dispose()
    {
        if (_webView == 0) return;
        try
        {
            if (_subscribed)
            {
                _ = ((delegate* unmanaged[Stdcall]<nint, long, int>)Method(_webView, RemoveRequested))(_webView, _token);
                // Do not remove shared filters here: a replacement backend may already have
                // installed the same patterns. They are harmless without our event handler.
            }
        }
        finally
        {
            if (_response != 0) Marshal.Release(_response);
            Marshal.Release(_webView);
            _response = _webView = 0;
        }
    }

    private static nint Method(nint instance, int slot) => (*(nint**)instance)[slot];
    private static nint GetObject(nint instance, int slot)
    {
        nint result;
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Method(instance, slot))(instance, &result));
        return result;
    }

    internal static bool ShouldBlockRequest(BrowserAdBlockRules rules, Uri uri, Uri page, int context,
        string? fetchSite, string? fetchDestination)
    {
        string type = context switch
        {
            1 => "document",
            2 => "style-sheet",
            3 => "image",
            4 => "media",
            5 => "font",
            6 => "script",
            _ => "raw"
        };
        bool? thirdParty = fetchSite switch
        {
            "same-origin" or "same-site" => false,
            "cross-site" => true,
            _ => null
        };
        // Document context also includes top-level navigations and redirects. Only
        // explicit Fetch Metadata identifies a child frame; a different URL does not.
        bool isChildFrame = context == 1 && fetchDestination is "iframe" or "frame";
        return rules.ShouldBlock(uri, page, type, isChildFrame, thirdParty);
    }

    [GeneratedComClass]
    internal sealed partial class RequestHandler(WindowsAdBlockBackend owner, BrowserAdBlockRules rules, Func<Uri> getPage) : IAdBlockRequestHandler
    {
        public void Invoke(nint sender, nint args)
        {
            if (owner._response == 0) return;
            nint request = 0, uriText = 0;
            try
            {
                request = GetObject(args, 3);
                uriText = GetObject(request, 3);
                if (!Uri.TryCreate(Marshal.PtrToStringUni(uriText), UriKind.Absolute, out Uri? uri)) return;
                int context;
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, int*, int>)Method(args, 7))(args, &context));
                if (ShouldBlockRequest(rules, uri, getPage(), context,
                        GetHeader(request, "Sec-Fetch-Site"), GetHeader(request, "Sec-Fetch-Dest")))
                    Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, int>)Method(args, 5))(args, owner._response));
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceError("Ad filter request failed: {0}", ex.Message); }
            finally
            {
                if (uriText != 0) Marshal.FreeCoTaskMem(uriText);
                if (request != 0) Marshal.Release(request);
            }
        }

        private static string? GetHeader(nint request, string headerName)
        {
            nint headers = 0, value = 0;
            try
            {
                headers = GetObject(request, 9);
                fixed (char* name = headerName)
                {
                    nint nativeValue;
                    int hr = ((delegate* unmanaged[Stdcall]<nint, char*, nint*, int>)Method(headers, 3))(headers, name, &nativeValue);
                    if (hr < 0) return null;
                    value = nativeValue;
                }
                return Marshal.PtrToStringUni(value);
            }
            finally
            {
                if (value != 0) Marshal.FreeCoTaskMem(value);
                if (headers != 0) Marshal.Release(headers);
            }
        }
    }
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("AB00B74C-15F1-4646-80E8-E76341D25D71")]
internal partial interface IAdBlockRequestHandler
{
    void Invoke(nint sender, nint args);
}
