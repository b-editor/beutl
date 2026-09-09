using Beutl.Api.Services;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Services.AI;

internal static class AiVideoResultDownload
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(AiVideoResultDownload));

    public static async Task<string> DownloadAsync(
        IAuthenticatedContentService content,
        Uri contentUri,
        AiContentMetadata? declaredMetadata,
        CancellationToken cancellationToken)
    {
        (string stagingPath, FileStream destination) = AiTemporaryFileStore.Create(
            "results", "ai-video", ".download");
        string ownedPath = stagingPath;
        try
        {
            AiContentDownload download;
            await using (destination)
            {
                using Stream bounded = CreateBoundedStream(destination);
                download = await content.CopyToAsync(contentUri, bounded, cancellationToken);
            }

            AiContentMetadata? metadata = AiContentMetadata.Combine(declaredMetadata, download.Metadata);
            string extension = metadata?.GetFileExtension(".mp4", "video") ?? ".mp4";
            string completedPath = Path.ChangeExtension(stagingPath, extension);
            File.Move(stagingPath, completedPath);
            ownedPath = completedPath;
            AiTemporaryFileStore.EnsurePrivateFile(completedPath);
            return completedPath;
        }
        catch
        {
            try
            {
                File.Delete(ownedPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                s_logger.LogWarning(ex, "Failed to remove incomplete AI video {Path}.", ownedPath);
            }
            throw;
        }
    }

    // The API accepts at most 60 seconds. This still permits an encoded bitrate above 68 Mbit/s
    // while bounding a malformed or non-terminating response well below the whole disk.
    internal const long MaximumBytes = 512L * 1024 * 1024;

    public static Stream CreateBoundedStream(
        Stream destination,
        long maximumBytes = MaximumBytes)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        if (maximumBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));

        return new BoundedWriteStream(destination, maximumBytes);
    }

    private sealed class BoundedWriteStream(Stream destination, long maximumBytes) : Stream
    {
        private long _written;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => destination.CanWrite;

        public override long Length => _written;

        public override long Position
        {
            get => _written;
            set => throw new NotSupportedException();
        }

        public override void Flush() => destination.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken)
            => destination.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCapacity(count);
            destination.Write(buffer, offset, count);
            _written += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCapacity(buffer.Length);
            destination.Write(buffer);
            _written += buffer.Length;
        }

        public override async Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            EnsureCapacity(count);
            await destination.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
            _written += count;
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            EnsureCapacity(buffer.Length);
            await destination.WriteAsync(buffer, cancellationToken);
            _written += buffer.Length;
        }

        public override void WriteByte(byte value)
        {
            EnsureCapacity(1);
            destination.WriteByte(value);
            _written++;
        }

        protected override void Dispose(bool disposing)
        {
            // The caller owns the staging file stream. This wrapper only bounds writes.
            base.Dispose(disposing);
        }

        private void EnsureCapacity(int count)
        {
            if (count < 0 || count > maximumBytes - _written)
            {
                throw new InvalidDataException(
                    $"The AI video result exceeds the {maximumBytes}-byte download limit.");
            }
        }
    }
}
