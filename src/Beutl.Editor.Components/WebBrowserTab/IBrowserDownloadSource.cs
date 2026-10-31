namespace Beutl.Editor.Components.WebBrowserTab;

internal interface IBrowserDownloadSource : IDisposable
{
    // A page script generated the file as a blob: or data: URL. That address cannot be requested again, so the
    // download history records the page instead and offers no retry.
    bool IsGenerated => false;

    Task<string> SaveAsync(string directory, IProgress<(long Received, long? Total)>? progress, CancellationToken cancellationToken);
}
