using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using Avalonia.Platform;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

using static Beutl.Editor.Components.WebBrowserTab.LinuxWebKit;

namespace Beutl.Editor.Components.WebBrowserTab;

// Avalonia's WebKitGTK and WPE adapters expose no cookie manager, so a download reads the session's cookies
// from WebKit itself, as the WebView2 cookie manager provides them on Windows. Without them a download is a
// stranger to the site the user is signed in to.
internal static partial class LinuxWebKitCookies
{
    internal static async Task<IReadOnlyList<Cookie>> GetAllAsync(IPlatformHandle? handle)
    {
        try
        {
            return handle switch
            {
                IGtkWebViewPlatformHandle gtk when OperatingSystem.IsLinux() => await GetAllAsync(Api.Gtk.Value, gtk.WebKitWebView),
                ILinuxWpePlatformHandle wpe when OperatingSystem.IsLinux() => await GetAllAsync(Api.Wpe.Value, wpe.WebKitWebView),
                _ => []
            };
        }
        catch (Exception ex)
        {
            // The download then goes without cookies, as Linux downloads did before WebKit was asked for them.
            Log.CreateLogger(typeof(LinuxWebKitCookies)).LogWarning(ex, "Could not read the WebKit cookies for a download.");
            return [];
        }
    }

    // WebKit keeps cookies in memory unless the application names a store, so a restart would sign the user out.
    internal static Task UsePersistentStorageAsync(IPlatformHandle? handle, string path) => handle switch
    {
        IGtkWebViewPlatformHandle gtk when OperatingSystem.IsLinux() => UsePersistentStorageAsync(Api.Gtk.Value, gtk.WebKitWebView, path),
        ILinuxWpePlatformHandle wpe when OperatingSystem.IsLinux() => UsePersistentStorageAsync(Api.Wpe.Value, wpe.WebKitWebView, path),
        _ => Task.CompletedTask
    };

    private static async Task UsePersistentStorageAsync(Api api, nint webView, string path)
    {
        ObjectRef(webView);
        try { await OnGlibAsync(() => api.UsePersistentStorage(webView, path)); }
        finally { await OnGlibAsync(() => ObjectUnref(webView)); }
    }

    private static async Task<IReadOnlyList<Cookie>> GetAllAsync(Api api, nint webView)
    {
        // webkit_cookie_manager_get_all_cookies exists since WebKit 2.42.
        if (!api.CanListCookies) return [];
        var completion = new TaskCompletionSource<IReadOnlyList<Cookie>>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Retain before dispatch; release on GLib too because the last unref can destroy a widget.
        ObjectRef(webView);
        try { await OnGlibAsync(() => api.BeginGetAllCookies(webView, completion)); }
        finally { await OnGlibAsync(() => ObjectUnref(webView)); }
        return await completion.Task;
    }

    private sealed unsafe class Api
    {
        // WebKitGTK 4.1 and WPE 2.0 link libsoup 3, WebKitGTK 4.0 libsoup 2.4. Loading the other libsoup aborts the process.
        internal static readonly Lazy<Api> Gtk = new(() => NativeLibrary.TryLoad("libwebkit2gtk-4.1.so.0", out nint webkit)
            ? new(webkit, "libsoup-3.0.so.0", false) : new(LoadLibrary(GtkLibraries), "libsoup-2.4.so.1", false));
        internal static readonly Lazy<Api> Wpe = new(() => new(LoadLibrary(WpeLibraries), "libsoup-3.0.so.0", true));
        // Only touched on the GLib thread. Naming the store again would drop the session cookies kept in memory.
        private static readonly HashSet<nint> s_persistentManagers = [];
        private readonly delegate* unmanaged[Cdecl]<nint, nint> _getStore, _getCookieManager;
        private readonly delegate* unmanaged[Cdecl]<nint, nint, int, void> _setPersistentStorage;
        private readonly delegate* unmanaged[Cdecl]<nint, nint, nint, nint, void> _getAllCookies;
        private readonly delegate* unmanaged[Cdecl]<nint, nint, nint*, nint> _getAllCookiesFinish;
        private readonly delegate* unmanaged[Cdecl]<nint, nint> _getName, _getValue, _getDomain, _getPath;
        private readonly delegate* unmanaged[Cdecl]<nint, int> _getSecure, _getHttpOnly;
        private readonly delegate* unmanaged[Cdecl]<nint, void> _freeCookie;

        private Api(nint webkit, string soupLibrary, bool wpe)
        {
            nint soup = NativeLibrary.Load(soupLibrary);
            // The 2022 API keeps cookies in the network session; WebKitGTK 4.x in the website data manager.
            _getStore = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit,
                wpe ? "webkit_web_view_get_network_session" : "webkit_web_view_get_website_data_manager");
            _getCookieManager = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit,
                wpe ? "webkit_network_session_get_cookie_manager" : "webkit_website_data_manager_get_cookie_manager");
            _setPersistentStorage = (delegate* unmanaged[Cdecl]<nint, nint, int, void>)NativeLibrary.GetExport(webkit, "webkit_cookie_manager_set_persistent_storage");
            if (NativeLibrary.TryGetExport(webkit, "webkit_cookie_manager_get_all_cookies", out nint getAll))
            {
                _getAllCookies = (delegate* unmanaged[Cdecl]<nint, nint, nint, nint, void>)getAll;
                _getAllCookiesFinish = (delegate* unmanaged[Cdecl]<nint, nint, nint*, nint>)NativeLibrary.GetExport(webkit, "webkit_cookie_manager_get_all_cookies_finish");
            }
            _getName = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(soup, "soup_cookie_get_name");
            _getValue = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(soup, "soup_cookie_get_value");
            _getDomain = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(soup, "soup_cookie_get_domain");
            _getPath = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(soup, "soup_cookie_get_path");
            _getSecure = (delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(soup, "soup_cookie_get_secure");
            _getHttpOnly = (delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(soup, "soup_cookie_get_http_only");
            _freeCookie = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(soup, "soup_cookie_free");
        }

        internal bool CanListCookies => _getAllCookies != null;

        internal void UsePersistentStorage(nint webView, string path)
        {
            nint manager = _getCookieManager(_getStore(webView));
            if (!s_persistentManagers.Add(manager)) return;
            nint text = Marshal.StringToCoTaskMemUTF8(path);
            try { _setPersistentStorage(manager, text, 1 /* WEBKIT_COOKIE_PERSISTENT_STORAGE_SQLITE */); }
            finally { Marshal.FreeCoTaskMem(text); }
        }

        internal void BeginGetAllCookies(nint webView, TaskCompletionSource<IReadOnlyList<Cookie>> completion)
        {
            nint manager = _getCookieManager(_getStore(webView));
            var state = GCHandle.Alloc((this, completion));
            _getAllCookies(manager, 0, (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnCookies, GCHandle.ToIntPtr(state));
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnCookies(nint manager, nint result, nint data)
        {
            var handle = GCHandle.FromIntPtr(data);
            var (api, completion) = ((Api, TaskCompletionSource<IReadOnlyList<Cookie>>))handle.Target!;
            handle.Free();
            try { completion.TrySetResult(api.Finish(manager, result)); }
            catch (Exception ex) { completion.TrySetException(ex); }
        }

        private List<Cookie> Finish(nint manager, nint result)
        {
            nint error = 0;
            nint list = _getAllCookiesFinish(manager, result, &error);
            var cookies = new List<Cookie>();
            if (list == 0)
            {
                // The download then goes without cookies, as it did before WebKit could list them.
                if (error != 0) ErrorFree(error);
                return cookies;
            }
            try
            {
                // GList: data, next, prev.
                for (nint node = list; node != 0; node = ((nint*)node)[1])
                {
                    nint cookie = ((nint*)node)[0];
                    // WebKit lists a cookie it cannot convert from libsoup 2.4's raw bytes as NULL.
                    if (cookie == 0) continue;
                    try
                    {
                        string? name = Marshal.PtrToStringUTF8(_getName(cookie));
                        string value = Marshal.PtrToStringUTF8(_getValue(cookie)) ?? "";
                        string? domain = Marshal.PtrToStringUTF8(_getDomain(cookie));
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(domain)) continue;
                        // HttpClient refuses a header with non-ASCII text, which would fail the whole download.
                        if (!Ascii.IsValid(name) || !Ascii.IsValid(value)) continue;
                        cookies.Add(new Cookie(name, value,
                            Marshal.PtrToStringUTF8(_getPath(cookie)) ?? "/", domain)
                        {
                            Secure = _getSecure(cookie) != 0,
                            HttpOnly = _getHttpOnly(cookie) != 0
                        });
                    }
                    catch (CookieException)
                    {
                        // WebKit accepted a cookie that System.Net cannot represent; a browser would still send it.
                    }
                    finally { _freeCookie(cookie); }
                }
            }
            finally { ListFree(list); }
            return cookies;
        }
    }

    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_list_free")] private static partial void ListFree(nint list);
}
