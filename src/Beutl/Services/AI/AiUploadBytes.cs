using Beutl.Api.Services;

namespace Beutl.Services.AI;

/// <summary>
/// Reads a file that is about to be uploaded. The file may have grown since it was chosen, so this never
/// reads past <c>limit</c>.
/// </summary>
internal static class AiUploadBytes
{
    public static async Task<byte[]> ReadWithinAsync(
        string path,
        long limit,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > limit)
            throw new AiFileTooLargeException();

        // Read to one byte past the limit rather than trusting the length: a
        // file being written to grows between the two.
        var buffer = new MemoryStream(capacity: (int)Math.Min(stream.Length, limit));
        byte[] chunk = new byte[81920];
        long read = 0;
        while (true)
        {
            int count = await stream.ReadAsync(chunk, cancellationToken);
            if (count == 0)
                break;

            read += count;
            if (read > limit)
                throw new AiFileTooLargeException();
            buffer.Write(chunk, 0, count);
        }

        return buffer.ToArray();
    }
}
