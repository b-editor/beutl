using Beutl.Api.Services;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Services.AI;

/// <summary>Fetches a finished AI picture and checks it before anything decodes it.</summary>
internal static class AiImageResultDownload
{
    /// <summary>The encoded picture exactly as the server produced it, validated.</summary>
    public static async Task<byte[]> DownloadEncodedAsync(
        IAuthenticatedContentService content,
        Uri contentUri,
        CancellationToken cancellationToken)
    {
        using MemoryStream stream = await DownloadAsync(content, contentUri, cancellationToken);
        return stream.ToArray();
    }

    public static async Task<Ref<Bitmap>> DownloadBitmapAsync(
        IAuthenticatedContentService content,
        Uri contentUri,
        CancellationToken cancellationToken)
    {
        using MemoryStream stream = await DownloadAsync(content, contentUri, cancellationToken);
        return Ref<Bitmap>.Create(Bitmap.FromStream(stream));
    }

    private static async Task<MemoryStream> DownloadAsync(
        IAuthenticatedContentService content,
        Uri contentUri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(contentUri);
        var stream = new SizeLimitedMemoryStream(checked((int)AiRequestLimits.MaxImageUploadBytes));
        try
        {
            await content.CopyToAsync(contentUri, stream, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            stream.Position = 0;
            AiImageDecodeValidator.ValidateEncoded(stream, AiRequestLimits.MaxImageUploadBytes);
            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}
