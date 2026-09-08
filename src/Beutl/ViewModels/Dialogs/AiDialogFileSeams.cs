namespace Beutl.ViewModels.Dialogs;

/// <summary>
/// The result of an AI dialog save picker. The picker and the file replacement
/// are kept as separate steps so identity fencing can reject a late destination
/// before it mutates that destination.
/// </summary>
internal sealed record AiSaveFileDestination(string Path);

internal static class AiImageFileFormat
{
    public static void ValidatePngDestination(string path)
    {
        if (!string.Equals(
                System.IO.Path.GetExtension(path),
                ".png",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "AI-generated PNG images must be saved with a .png extension.");
        }
    }
}

internal static class AiAtomicFileWriter
{
    public static void Write(
        string destinationPath,
        Action<Stream> write,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(write);

        string fullDestinationPath = System.IO.Path.GetFullPath(destinationPath);
        string temporaryPath = fullDestinationPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            UnixFileMode destinationMode = default;
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 64 * 1024,
                Options = FileOptions.WriteThrough,
            };
            if (!OperatingSystem.IsWindows())
            {
                destinationMode = File.Exists(fullDestinationPath)
                    ? File.GetUnixFileMode(fullDestinationPath)
                    : UnixFileMode.UserRead | UnixFileMode.UserWrite;
                options.UnixCreateMode = destinationMode;
            }

            using (var stream = new FileStream(
                       temporaryPath,
                       options))
            {
                write(stream);
                cancellationToken.ThrowIfCancellationRequested();
                stream.Flush(true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporaryPath, destinationMode);
            File.Move(temporaryPath, fullDestinationPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
