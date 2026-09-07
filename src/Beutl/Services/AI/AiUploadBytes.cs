using Beutl.Api.Services;

namespace Beutl.Services.AI;

/// <summary>
/// Reading a file that is about to be uploaded, without letting it decide how
/// much memory that takes.
/// </summary>
/// <remarks>
/// A request is named by what its file contains, so the bytes have to be in
/// hand before the request goes out. What is on disk can have changed since it
/// was chosen, though — the same path can hold something enormous by the time
/// it is read — and the size the request is allowed is known up front. Reading
/// past it serves no purpose and is how a picture picked at ten megabytes turns
/// into a process with none left.
/// </remarks>
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
