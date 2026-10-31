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

// Avalonia's WebKitGTK and WPE adapters expose no cookie manager, so a download reads the session's cookies
// from WebKit itself, as the WebView2 cookie manager provides them on Windows. Without them a download is a
// stranger to the site the user is signed in to.
internal static partial class LinuxWebKitCookies
{
    internal static async Task<IReadOnlyList<Cookie>> GetAllAsync(IPlatformHandle? handle)
    {
        try
        {
            return TryGetWebView(handle, out nint webView, out bool wpe)
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

        private static Api Create(bool wpe)
        {
            (nint webkit, nint soup) = LoadWithSoup(wpe);
            return new Api(webkit, soup, wpe);
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
