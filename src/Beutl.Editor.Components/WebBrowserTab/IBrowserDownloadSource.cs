namespace Beutl.Editor.Components.WebBrowserTab;

internal interface IBrowserDownloadSource : IDisposable
{
    // A page script generated the file as a blob: or data: URL. That address cannot be requested again, so the
    // download history records the site credited with the file instead and offers no retry.
    bool IsGenerated => false;

    // The method of the request the engine received this response for, such as GET or a form's POST, or null when the
    // engine does not report it. Sending a request other than a GET again could submit a form twice, so the download
    // history offers no retry for one, and an offer to extensions counts an unknown method as not requestable again.
    string? RequestMethod => null;

    Task<string> SaveAsync(string directory, IProgress<(long Received, long? Total)>? progress, CancellationToken cancellationToken);
}
