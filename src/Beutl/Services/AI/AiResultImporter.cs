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
    private readonly Scene _scene;
    private readonly IElementAdder _elementAdder;
    private readonly ILogger _logger = Log.CreateLogger<AiResultImporter>();

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
            temporaryPath =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                bitmap.Save(temporaryPath, EncodedImageFormat.Png);
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
            async temporaryPath =>
            {
                await File.WriteAllBytesAsync(temporaryPath, bytes.ToArray(), cancellationToken);
            },
            options,
            cancellationToken);

    public Task<ElementAddResult> ImportVideoAsync(
        string sourcePath,
        AiResultImportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        return ImportVideoCoreAsync(
            async temporaryPath =>
            {
                await using FileStream source = File.OpenRead(sourcePath);
                await using FileStream destination = new(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true);
                await source.CopyToAsync(destination, cancellationToken);
            },
            options,
            cancellationToken);
    }

    private async Task<ElementAddResult> ImportVideoCoreAsync(
        Func<string, Task> writer,
        AiResultImportOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        string path = await StageAsync(".mp4", writer, cancellationToken);
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
        Func<string, Task> writer,
        CancellationToken cancellationToken)
    {
        string directory = GetResourceDirectory();
        Directory.CreateDirectory(directory);

        string destinationPath = Path.Combine(directory, $"{Guid.NewGuid():N}{extension}");
        string temporaryPath = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer(temporaryPath);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destinationPath);
            return destinationPath;
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private string GetResourceDirectory()
    {
        string projectDirectory = _scene.Uri?.LocalPath is { } scenePath
            ? Path.GetDirectoryName(scenePath)!
            : Path.Combine(Path.GetTempPath(), "Beutl", "Unsaved", _scene.Id.ToString("N"));
        return Path.Combine(projectDirectory, "resources", "ai");
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove unused AI project resource {Path}.", path);
        }
    }
}
