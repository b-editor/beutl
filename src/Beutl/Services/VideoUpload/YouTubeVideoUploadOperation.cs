using Google.Apis.Upload;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace Beutl.Services.VideoUpload;

internal sealed class YouTubeVideoUploadOperation(
    string filePath, Video metadata, Func<YouTubeService> createService) : IVideoUploadOperation
{
    public async Task<Uri?> UploadAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (file.Length == 0)
            throw new InvalidOperationException(Strings.VideoUpload_EmptyFile);

        using YouTubeService service = createService();
        var upload = service.Videos.Insert(metadata, "snippet,status", file, "application/octet-stream");
        upload.ChunkSize = 8 * 1024 * 1024;
        upload.ProgressChanged += value => progress.Report((double)value.BytesSent / file.Length);
        IUploadProgress result = await upload.UploadAsync(cancellationToken);
        // The SDK can return a Failed result (including during session creation) instead of throwing.
        // A server-acknowledged completion wins over a cancellation arriving just afterwards.
        if (result.Status != UploadStatus.Completed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw result.Exception ?? new IOException(Strings.VideoUpload_Failed);
        }

        if (string.IsNullOrWhiteSpace(upload.ResponseBody?.Id))
            throw new IOException(Strings.VideoUpload_MissingReceipt);

        progress.Report(1);
        return new Uri("https://www.youtube.com/watch?v=" + Uri.EscapeDataString(upload.ResponseBody.Id));
    }

    public void Dispose() { }
}
