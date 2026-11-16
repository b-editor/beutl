namespace Beutl.Editor.Components.WebBrowserTab;

internal interface IBrowserDownloadSource : IDisposable
{
    // A page script generated the file as a blob: or data: URL. That address cannot be requested again, so the
    // download history records the site credited with the file instead and offers no retry.
    bool IsGenerated => false;

    // The method of the request the engine received this response for, when it was not a GET, such as a form's POST.
    // Sending that request again could submit the form twice, so the download history offers no retry either.
    string? RequestMethod => null;

    Task<string> SaveAsync(string directory, IProgress<(long Received, long? Total)>? progress, CancellationToken cancellationToken);
}
