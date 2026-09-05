namespace Beutl.Api.Services;

internal static class AiUploadValidation
{
    public static async ValueTask<Stream> OpenAsync(
        AiUploadSource source,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        Stream stream = await source.OpenReadAsync(cancellationToken);
        MemoryStream? buffered = null;
        try
        {
            if (stream.CanSeek)
            {
                long remaining = stream.Length - stream.Position;
                if (remaining != source.Length || remaining > maximumBytes)
                    throw new AiFileTooLargeException();
                return stream;
            }

            buffered = new MemoryStream();
            byte[] buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                int read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                    break;
                total += read;
                if (total > source.Length || total > maximumBytes)
                    throw new AiFileTooLargeException();
                await buffered.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            if (total != source.Length)
                throw new AiFileTooLargeException();
            buffered.Position = 0;
            await stream.DisposeAsync();
            return buffered;
        }
        catch
        {
            await stream.DisposeAsync();
            if (buffered is not null)
                await buffered.DisposeAsync();
            throw;
        }
    }
}
