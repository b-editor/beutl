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

    internal bool IsCompleteGetResponse(Uri uri, nint statusCode) => statusCode == 200
        && _requestMethod == "GET" && _requestUri == uri
        && BrowserMediaDownload.IsHttpUri(uri);
}
