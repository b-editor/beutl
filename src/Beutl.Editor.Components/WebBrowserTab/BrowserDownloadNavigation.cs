namespace Beutl.Editor.Components.WebBrowserTab;

internal sealed class BrowserDownloadNavigation
{
    private Uri? _requestUri;
    private string? _requestMethod;

    internal void RecordRequest(Uri? uri, string? method, bool isMainFrame)
    {
        if (!isMainFrame) return;
        _requestUri = uri;
        _requestMethod = method;
    }

    // The method of the main-frame request a complete response answers, if WebKit may hand that response to a
    // download as it is; otherwise null. A form's POST is never sent again: WebKit transfers the response it already
    // received. WebKit decides each redirect as a new request, so a 303 after a POST is recorded as its GET, while a
    // 307 or 308 keeps the POST, with its body, at the address that answers.
    internal string? GetCompleteResponseMethod(Uri uri, nint statusCode) => statusCode == 200
        && _requestMethod is "GET" or "POST" && _requestUri == uri
        && BrowserMediaDownload.IsHttpUri(uri)
            ? _requestMethod
            : null;
}
