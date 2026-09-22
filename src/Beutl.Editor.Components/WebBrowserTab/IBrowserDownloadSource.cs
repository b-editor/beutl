namespace Beutl.Editor.Components.WebBrowserTab;

internal interface IBrowserDownloadSource : IDisposable
{
    Task<string> SaveAsync(string directory, IProgress<(long Received, long? Total)>? progress, CancellationToken cancellationToken);
}
