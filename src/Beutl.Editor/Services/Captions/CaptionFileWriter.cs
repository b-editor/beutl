namespace Beutl.Editor.Services.Captions;

internal static class CaptionFileWriter
{
    internal static async Task WriteAsync(
        string destinationPath, byte[] bytes, bool overwrite, CancellationToken cancellationToken)
    {
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
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporaryPath, destinationMode);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destinationPath, overwrite);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
