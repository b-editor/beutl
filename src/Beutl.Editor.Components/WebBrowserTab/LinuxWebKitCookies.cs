using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

using Avalonia.Controls;
using Avalonia.Platform;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

using static Beutl.Editor.Components.WebBrowserTab.LinuxWebKit;

namespace Beutl.Editor.Components.WebBrowserTab;

// Read and clear cookies directly in WebKit. GTK has no Avalonia cookie manager and WPE's manager
// can throw from a native callback when a cookie has no name.
internal static partial class LinuxWebKitCookies
{
    internal static async Task<IReadOnlyList<Cookie>> GetAllAsync(IPlatformHandle? handle)
    {
        try
        {
            return TryGetWebView(handle, out nint webView, out bool wpe) && webView != 0
                ? await GetAllAsync((wpe ? Api.Wpe : Api.Gtk).Value, webView)
                : [];
        }
        catch (Exception ex)
        {
            // The download then goes without cookies, as Linux downloads did before WebKit was asked for them.
            Log.CreateLogger(typeof(LinuxWebKitCookies)).LogWarning(ex, "Could not read the WebKit cookies for a download.");
            return [];
        }
    }

    // webkit_cookie_manager_get_all_cookies exists since WebKit 2.42; older versions list no cookies.
    internal static bool CanListCookies(IPlatformHandle? handle) =>
        TryGetWebView(handle, out _, out bool wpe) && (wpe ? Api.Wpe : Api.Gtk).Value.CanListCookies;

    internal static Func<Task>? CreateClearAction(IEnumerable<IPlatformHandle?> handles)
    {
        var views = new List<(Api Api, nint WebView)>();
        try
        {
            foreach (var handle in handles)
            {
                if (!TryGetWebView(handle, out nint webView, out bool wpe) || webView == 0) continue;
                Api api = (wpe ? Api.Wpe : Api.Gtk).Value;
                if (!api.CanClearCookies) return null;
                views.Add((api, webView));
            }
        }
        catch (Exception ex)
        {
            Log.CreateLogger(typeof(LinuxWebKitCookies)).LogWarning(ex, "Could not access WebKit cookie deletion.");
            return null;
        }

        return views.Count == 0 ? null : () => ClearAsync(views);
    }

    private static Task ClearAsync(List<(Api Api, nint WebView)> views)
    {
        // Multiple views can share the same store. Resolve and deduplicate their managers on GLib.
        var managers = new HashSet<nint>();
        return Task.WhenAll(views.Select(async view =>
        {
            Task pending = Task.CompletedTask;
            await OnGlibAsync(view.WebView, () =>
            {
                nint manager = view.Api.GetWebsiteDataManager(view.WebView);
                if (manager == 0) throw new InvalidOperationException(Strings.BrowserCookiesUnsupported);
                if (managers.Add(manager)) pending = view.Api.BeginClearCookies(manager);
            });
            await pending;
        }));
    }

    // WebView2 keeps cookies in its profile folder, but WebKitGTK and WPE keep them in memory unless the
    // application names a store, so restarting Beutl would sign the user out of every site.
    internal static void PersistCookies(object? sender, WebViewAdapterEventArgs e)
    {
        if (OperatingSystem.IsLinux() && e.TryGetPlatformHandle() is { } handle) _ = PersistCookiesAsync(handle);
    }

    [SupportedOSPlatform("linux")]
    private static async Task PersistCookiesAsync(IPlatformHandle handle)
    {
        string path = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "browser", "WebKit", "cookies.sqlite");
        try
        {
            // The store holds the user's sign-ins. Like a browser profile, only the user may enter its folder,
            // which also covers the SQLite journal files WebKit creates beside it.
            const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            string directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory, ownerOnly);
            File.SetUnixFileMode(directory, ownerOnly);
            await UsePersistentStorageAsync(handle, path);
        }
        catch (Exception ex)
        {
            // Cookies then last until Beutl exits, as they did before.
            Log.CreateLogger(typeof(LinuxWebKitCookies)).LogWarning(ex, "Could not keep WebKit cookies in {Path}.", path);
        }
    }

    private static Task UsePersistentStorageAsync(IPlatformHandle handle, string path)
    {
        if (!TryGetWebView(handle, out nint webView, out bool wpe)) return Task.CompletedTask;
        Api api = (wpe ? Api.Wpe : Api.Gtk).Value;
        return OnGlibAsync(webView, () => api.UsePersistentStorage(webView, path));
    }

    private static async Task<IReadOnlyList<Cookie>> GetAllAsync(Api api, nint webView)
    {
        // webkit_cookie_manager_get_all_cookies exists since WebKit 2.42.
        if (!api.CanListCookies) return [];
        var completion = new TaskCompletionSource<IReadOnlyList<Cookie>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await OnGlibAsync(webView, () => api.BeginGetAllCookies(webView, completion));
        return await completion.Task;
    }

    private sealed unsafe class Api
    {
        internal static readonly Lazy<Api> Gtk = new(() => Create(wpe: false));
        internal static readonly Lazy<Api> Wpe = new(() => Create(wpe: true));
        // Only touched on the GLib thread. Naming the store again would drop the session cookies kept in memory.
        private static readonly HashSet<nint> s_persistentManagers = [];
        private readonly delegate* unmanaged[Cdecl]<nint, nint> _getStore, _getCookieManager;
        private readonly delegate* unmanaged[Cdecl]<nint, nint, int, void> _setPersistentStorage;
        private readonly delegate* unmanaged[Cdecl]<nint, nint, nint, nint, void> _getAllCookies;
        private readonly delegate* unmanaged[Cdecl]<nint, nint, nint*, nint> _getAllCookiesFinish;
        private readonly delegate* unmanaged[Cdecl]<nint, nint> _getSessionWebsiteDataManager;
        private readonly delegate* unmanaged[Cdecl]<nint, uint, long, nint, delegate* unmanaged[Cdecl]<nint, nint, nint, void>, nint, void> _clear;
        private readonly delegate* unmanaged[Cdecl]<nint, nint, GError**, int> _clearFinish;
        private readonly uint _cookieDataType;
        private readonly delegate* unmanaged[Cdecl]<nint, nint> _getName, _getValue, _getDomain, _getPath;
        private readonly delegate* unmanaged[Cdecl]<nint, int> _getSecure, _getHttpOnly;
        private readonly delegate* unmanaged[Cdecl]<nint, void> _freeCookie;

        private Api(nint webkit, nint soup, bool wpe)
        {
            // The 2022 API keeps cookies in the network session; WebKitGTK 4.x in the website data manager.
            _getStore = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit,
                wpe ? "webkit_web_view_get_network_session" : "webkit_web_view_get_website_data_manager");
            _getCookieManager = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit,
                wpe ? "webkit_network_session_get_cookie_manager" : "webkit_website_data_manager_get_cookie_manager");
            // The 2022 API removed legacy data types and shifted WEBKIT_WEBSITE_DATA_COOKIES.
            _cookieDataType = wpe ? 1u << 6 : 1u << 8;
            _setPersistentStorage = (delegate* unmanaged[Cdecl]<nint, nint, int, void>)NativeLibrary.GetExport(webkit, "webkit_cookie_manager_set_persistent_storage");
            if (NativeLibrary.TryGetExport(webkit, "webkit_cookie_manager_get_all_cookies", out nint getAll))
            {
                _getAllCookies = (delegate* unmanaged[Cdecl]<nint, nint, nint, nint, void>)getAll;
                _getAllCookiesFinish = (delegate* unmanaged[Cdecl]<nint, nint, nint*, nint>)NativeLibrary.GetExport(webkit, "webkit_cookie_manager_get_all_cookies_finish");
            }
            nint getSessionWebsiteDataManager = 0;
            if (NativeLibrary.TryGetExport(webkit, "webkit_website_data_manager_clear", out nint clear)
                && NativeLibrary.TryGetExport(webkit, "webkit_website_data_manager_clear_finish", out nint clearFinish)
                && (!wpe || NativeLibrary.TryGetExport(webkit, "webkit_network_session_get_website_data_manager", out getSessionWebsiteDataManager)))
            {
                _getSessionWebsiteDataManager = (delegate* unmanaged[Cdecl]<nint, nint>)getSessionWebsiteDataManager;
                _clear = (delegate* unmanaged[Cdecl]<nint, uint, long, nint, delegate* unmanaged[Cdecl]<nint, nint, nint, void>, nint, void>)clear;
                _clearFinish = (delegate* unmanaged[Cdecl]<nint, nint, GError**, int>)clearFinish;
            }
            _getName = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(soup, "soup_cookie_get_name");
            _getValue = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(soup, "soup_cookie_get_value");
            _getDomain = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(soup, "soup_cookie_get_domain");
            _getPath = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(soup, "soup_cookie_get_path");
            _getSecure = (delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(soup, "soup_cookie_get_secure");
            _getHttpOnly = (delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(soup, "soup_cookie_get_http_only");
            _freeCookie = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(soup, "soup_cookie_free");
        }

        private static Api Create(bool wpe)
        {
            (nint webkit, nint soup) = LoadWithSoup(wpe);
            return new Api(webkit, soup, wpe);
        }

        internal bool CanListCookies => _getAllCookies != null;
        internal bool CanClearCookies => _clear != null;

        internal nint GetWebsiteDataManager(nint webView)
        {
            nint store = _getStore(webView);
            return store != 0 && _getSessionWebsiteDataManager != null ? _getSessionWebsiteDataManager(store) : store;
        }

        internal Task BeginClearCookies(nint manager)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var state = GCHandle.Alloc((this, completion));
            // A zero timespan deletes session and persisted cookies, including those .NET cannot represent.
            _clear(manager, _cookieDataType, 0, 0, &OnCookiesCleared, GCHandle.ToIntPtr(state));
            return completion.Task;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnCookiesCleared(nint manager, nint result, nint data)
        {
            TaskCompletionSource? completion = null;
            try
            {
                var handle = GCHandle.FromIntPtr(data);
                var (api, pending) = ((Api, TaskCompletionSource))handle.Target!;
                completion = pending;
                handle.Free();
                api.FinishClearCookies(manager, result);
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                // GLib cannot accept managed exceptions from this callback.
                try { completion?.TrySetException(ex); }
                catch { }
            }
        }

        private void FinishClearCookies(nint manager, nint result)
        {
            GError* error = null;
            try
            {
                if (_clearFinish(manager, result, &error) != 0) return;
                string? message = error != null ? Marshal.PtrToStringUTF8(error->Message) : null;
                throw new InvalidOperationException(message ?? Strings.WebBrowserActionFailed);
            }
            finally { if (error != null) ErrorFree((nint)error); }
        }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct GError
    {
        public uint Domain;
        public int Code;
        public nint Message;
    }

    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_list_free")] private static partial void ListFree(nint list);
}
