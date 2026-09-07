using System.Buffers.Binary;
using Beutl.Api.Services;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;

namespace Beutl.Services.AI;

internal sealed record AiResultImportOptions(
    TimeSpan Start,
    TimeSpan Length,
    int Layer,
    string Name);

internal sealed class AiResultImporter
{
    private static readonly ILogger s_logger = Log.CreateLogger<AiResultImporter>();
    private readonly Scene _scene;
    private readonly IElementAdder _elementAdder;
    private readonly Func<string, CancellationToken, Task> _validateVideo;

    public AiResultImporter(
        Scene scene,
        IElementAdder elementAdder,
        Func<string, CancellationToken, Task>? validateVideo = null)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _elementAdder = elementAdder ?? throw new ArgumentNullException(nameof(elementAdder));
        _validateVideo = validateVideo ?? ValidateVideoAsync;
    }

    public async Task<ElementAddResult> ImportImageAsync(
        Bitmap bitmap,
        AiResultImportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(options);

        string path = await StageAsync(
            ".png",
            stream =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!bitmap.Save(stream, EncodedImageFormat.Png))
                    throw new IOException("Failed to encode the AI image as PNG.");
                return Task.CompletedTask;
            },
            cancellationToken);
        return await AddStagedResultAsync(path, options, cancellationToken);
    }

    public async Task<ElementAddResult> ImportImageAsync(
        ReadOnlyMemory<byte> bytes,
        AiResultImportOptions options,
        CancellationToken cancellationToken = default)
    {
        if (bytes.Length > AiRequestLimits.MaxImageUploadBytes)
            throw new InvalidDataException("AI image data is too large.");
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        AiImageDecodeValidator.ValidateEncoded(stream, AiRequestLimits.MaxImageUploadBytes);
        using Bitmap bitmap = Bitmap.FromStream(stream);
        return await ImportImageAsync(bitmap, options, cancellationToken);
    }

    public Task<ElementAddResult> ImportVideoAsync(
        ReadOnlyMemory<byte> bytes,
        AiResultImportOptions options,
        CancellationToken cancellationToken = default)
        => ImportVideoCoreAsync(
            async stream =>
            {
                await stream.WriteAsync(bytes, cancellationToken);
            },
            ".mp4",
            options,
            cancellationToken);

    public Task<ElementAddResult> ImportVideoAsync(
        string sourcePath,
        AiResultImportOptions options,
        CancellationToken cancellationToken = default)
        => ImportVideoAsync(
            sourcePath,
            Path.GetExtension(sourcePath),
            options,
            cancellationToken);

    public Task<ElementAddResult> ImportVideoAsync(
        string sourcePath,
        string extension,
        AiResultImportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        return ImportVideoCoreAsync(
            async destination =>
            {
                await using FileStream source = File.OpenRead(sourcePath);
                await source.CopyToAsync(destination, cancellationToken);
            },
            extension,
            options,
            cancellationToken);
    }

    private async Task<ElementAddResult> ImportVideoCoreAsync(
        Func<Stream, Task> writer,
        string extension,
        AiResultImportOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        string normalizedExtension = NormalizeVideoExtension(extension);
        string path = await StageAsync(normalizedExtension, writer, cancellationToken);
        try
        {
            await _validateVideo(path, cancellationToken);
            return await AddStagedResultAsync(path, options, cancellationToken);
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    private static Task ValidateVideoAsync(string path, CancellationToken cancellationToken)
    {
        ValidateVideoContainerSignature(path);
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using MediaReader reader = MediaReader.Open(
                    path,
                    new MediaOptions(MediaMode.Video) { PreferProxy = false });
                Ref<Bitmap>? firstFrame = null;
                try
                {
                    if (!reader.HasVideo
                        || !reader.ReadVideo(0, out firstFrame)
                        || firstFrame?.Value is null)
                    {
                        throw new InvalidDataException(
                            "The AI video result does not contain a decodable video frame.");
                    }
                }
                finally
                {
                    firstFrame?.Dispose();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    "The AI video result could not be decoded.",
                    ex);
            }
        }, cancellationToken);
    }

    internal static void ValidateVideoContainerSignature(string path)
    {
        using FileStream stream = File.OpenRead(path);
        string extension = Path.GetExtension(path);
        if (extension is ".mp4" or ".mov")
        {
            ValidateIsoBaseMediaFile(stream);
            return;
        }

        Span<byte> header = stackalloc byte[4];
        try
        {
            stream.ReadExactly(header);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("The AI video result is truncated.", ex);
        }

        if (header[0] != 0x1a
            || header[1] != 0x45
            || header[2] != 0xdf
            || header[3] != 0xa3)
        {
            throw new InvalidDataException(
                "The AI video result does not match its declared container format.");
        }
    }

    private static void ValidateIsoBaseMediaFile(Stream stream)
    {
        const int maximumBoxesToInspect = 4096;
        Span<byte> header = stackalloc byte[16];
        long length = stream.Length;
        for (int boxIndex = 0; boxIndex < maximumBoxesToInspect && stream.Position < length; boxIndex++)
        {
            long remaining = length - stream.Position;
            if (remaining < 8)
                throw new InvalidDataException("The AI video result has a truncated MP4 box header.");

            stream.ReadExactly(header[..8]);
            ulong boxSize = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
            int headerSize = 8;
            if (boxSize == 1)
            {
                if (remaining < 16)
                {
                    throw new InvalidDataException(
                        "The AI video result has a truncated extended MP4 box header.");
                }

                stream.ReadExactly(header[8..16]);
                boxSize = BinaryPrimitives.ReadUInt64BigEndian(header[8..16]);
                headerSize = 16;
            }
            else if (boxSize == 0)
            {
                boxSize = (ulong)remaining;
            }

            if (boxSize < (ulong)headerSize || boxSize > (ulong)remaining)
                throw new InvalidDataException("The AI video result has an invalid MP4 box size.");

            ReadOnlySpan<byte> type = header[4..8];
            bool isFileType = type.SequenceEqual("ftyp"u8)
                && boxSize >= (ulong)(headerSize + 8);
            if (isFileType || type.SequenceEqual("moov"u8) || type.SequenceEqual("mdat"u8))
                return;

            stream.Seek(checked((long)boxSize - headerSize), SeekOrigin.Current);
        }

        throw new InvalidDataException(
            "The AI video result does not match its declared container format.");
    }

    private async Task<ElementAddResult> AddStagedResultAsync(
        string path,
        AiResultImportOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            ElementAddResult result = await _elementAdder.AddAsync(
            [
                new ElementDescription(
                    options.Start,
                    options.Length,
                    options.Layer,
                    new ElementSource.File(path),
                    options.Name),
            ], cancellationToken);

            if (!result.IsSuccess)
            {
                TryDelete(path);
            }

            return result;
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    private async Task<string> StageAsync(
        string extension,
        Func<Stream, Task> writer,
        CancellationToken cancellationToken)
    {
        string directory = GetResourceDirectory();
        Directory.CreateDirectory(directory);

        string destinationPath = Path.Combine(directory, $"{Guid.NewGuid():N}{extension}");
        string temporaryPath = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var streamOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 81920,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            };
            if (!OperatingSystem.IsWindows())
                streamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(temporaryPath, streamOptions))
            {
                await writer(stream);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destinationPath);
            return destinationPath;
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    internal static string GetUnsavedSceneDirectory(Guid sceneId)
        => UnsavedSceneStorage.GetDirectory(sceneId);

    private string GetResourceDirectory()
    {
        string projectDirectory = _scene.Uri?.LocalPath is { } scenePath
            ? Path.GetDirectoryName(scenePath)!
            : GetUnsavedSceneDirectory(_scene.Id);
        return Path.Combine(projectDirectory, "resources", "ai");
    }

    private static string NormalizeVideoExtension(string extension)
    {
        string normalized = string.IsNullOrWhiteSpace(extension)
            ? ".mp4"
            : extension.StartsWith('.') ? extension.ToLowerInvariant() : $".{extension.ToLowerInvariant()}";
        return normalized is ".mp4" or ".webm" or ".mov" or ".mkv"
            ? normalized
            : throw new ArgumentException("The AI video format is unsupported.", nameof(extension));
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Failed to remove unused AI project resource {Path}.", path);
        }
    }
}
