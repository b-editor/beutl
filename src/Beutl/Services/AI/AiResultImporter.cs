using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Logging;
using Beutl.Media;
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

    public AiResultImporter(Scene scene, IElementAdder elementAdder)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _elementAdder = elementAdder ?? throw new ArgumentNullException(nameof(elementAdder));
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
                bitmap.Save(stream, EncodedImageFormat.Png);
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
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
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
        return await AddStagedResultAsync(path, options, cancellationToken);
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
        => Path.Combine(
            BeutlEnvironment.GetHomeDirectoryPath(),
            "tmp",
            "unsaved",
            sceneId.ToString("N"));

    internal static void CleanupUnsavedSceneResources(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (scene.Uri is not null)
            return;

        string directory = GetUnsavedSceneDirectory(scene.Id);
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(
                ex,
                "Failed to remove AI resources owned by unsaved scene {SceneId} from {Path}.",
                scene.Id,
                directory);
        }
    }

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
