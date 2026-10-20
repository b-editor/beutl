using Avalonia.Platform.Storage;

namespace Beutl.Services.AI;

internal static class CaptionExportStorage
{
    public static async Task WriteAsync(
        IStorageFile file,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        // Every desktop save picker hands out files with a local path.
        Uri path = file.Path;
        if (!path.IsFile || string.IsNullOrWhiteSpace(path.LocalPath))
            throw new NotSupportedException("Captions can only be exported to a local file.");

        string destinationPath = path.LocalPath;
        string temporaryPath = destinationPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            UnixFileMode destinationMode = default;
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough,
            };
            if (!OperatingSystem.IsWindows())
            {
                destinationMode = File.Exists(destinationPath)
                    ? File.GetUnixFileMode(destinationPath)
                    : UnixFileMode.UserRead | UnixFileMode.UserWrite;
                options.UnixCreateMode = destinationMode;
            }

            await using (var stream = new FileStream(temporaryPath, options))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporaryPath, destinationMode);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
