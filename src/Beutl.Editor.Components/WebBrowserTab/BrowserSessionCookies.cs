using System.Net;

namespace Beutl.Editor.Components.WebBrowserTab;

internal static class BrowserSessionCookies
{
    internal static CookieContainer CreateContainer(IReadOnlyList<Cookie> cookies, Uri source)
    {
        int capacity = Math.Max(300, cookies.Count);
        var container = new CookieContainer(capacity, capacity, 4096);
        if (!BrowserMediaDownload.IsHttpUri(source)) return container;
        foreach (Cookie cookie in cookies)
        {
            if (cookie.Expired || string.IsNullOrEmpty(cookie.Domain)) continue;
            try
            {
                string domain = new UriBuilder(Uri.UriSchemeHttps, cookie.Domain.TrimStart('.')).Uri.IdnHost;
                if (!source.IdnHost.Equals(domain, StringComparison.OrdinalIgnoreCase)
                    && !(cookie.Domain.StartsWith('.') && source.IdnHost.EndsWith('.' + domain, StringComparison.OrdinalIgnoreCase)))
                    continue;
                var copy = new Cookie(cookie.Name, cookie.Value, cookie.Path)
                {
                    Expires = cookie.Expires,
                    HttpOnly = cookie.HttpOnly,
                    Secure = cookie.Secure
                };
                // Native cookie stores prefix domain cookies with a dot. Keep plain domains
                // host-only: setting Cookie.Domain would otherwise also authorize subdomains.
                if (cookie.Domain.StartsWith('.'))
                {
                    copy.Domain = cookie.Domain;
                    container.Add(copy);
                }
                else
                {
                    var origin = new UriBuilder(Uri.UriSchemeHttps, cookie.Domain).Uri;
                    container.Add(origin, copy);
                }
            }
            catch (Exception ex) when (ex is CookieException or UriFormatException)
            {
                // Skip malformed entries without changing the browser's cookie store.
            }
        }
        return container;
    }
}
