using System.Net.Http.Headers;

namespace Beutl.Api.Services;

/// <summary>
/// Describes one upload independently of local filesystem access. Each stream returned by the
/// factory transfers ownership to the caller and is disposed after the upload attempt.
/// </summary>
public sealed class AiUploadSource
{
    private readonly Func<CancellationToken, ValueTask<Stream>> _openReadAsync;

    public AiUploadSource(
        string fileName,
        string mediaType,
        Func<CancellationToken, ValueTask<Stream>> openReadAsync,
        long? length = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentNullException.ThrowIfNull(openReadAsync);
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));
        string normalizedFileName = fileName.Trim();
        if (Path.GetFileName(normalizedFileName) != normalizedFileName)
            throw new ArgumentException("The upload filename cannot contain a path.", nameof(fileName));
        if (!MediaTypeHeaderValue.TryParse(mediaType.Trim(), out MediaTypeHeaderValue? parsedMediaType))
            throw new ArgumentException("The upload media type is invalid.", nameof(mediaType));

        FileName = normalizedFileName;
        MediaType = parsedMediaType.ToString();
        Length = length;
        _openReadAsync = openReadAsync;
    }

    public string FileName { get; }

    public string MediaType { get; }

    public long? Length { get; }

    public static AiUploadSource FromFile(string filePath)
        => FromFile(filePath, Path.GetFileName(Path.GetFullPath(filePath)));

    /// <summary>
    /// The same file sent under a name of the caller's choosing.
    /// </summary>
    /// <remarks>
    /// The server fingerprints a request by, among other things, the name the
    /// file was uploaded under, and a request that repeats an idempotency key
    /// with a different fingerprint is refused. A caller that wants a retry to
    /// recover the result it already paid for therefore has to send the same
    /// name every time — which a temporary path, named for uniqueness on disk,
    /// cannot do.
    /// </remarks>
    public static AiUploadSource FromFile(string filePath, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string fullPath = Path.GetFullPath(filePath);
        long length = new FileInfo(fullPath).Length;
        return new AiUploadSource(
            fileName,
            AiMediaTypes.Get(fullPath),
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Stream stream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                return ValueTask.FromResult(stream);
            },
            length);
    }

    /// <summary>
    /// Opens a readable stream for this upload. The caller owns the returned stream and must dispose it.
    /// </summary>
    public async ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream? stream = await _openReadAsync(cancellationToken);
        if (stream is null)
            throw new InvalidOperationException("The upload source returned no stream.");
        if (!stream.CanRead)
        {
            await stream.DisposeAsync();
            throw new InvalidOperationException("The upload source returned an unreadable stream.");
        }

        return stream;
    }
}
