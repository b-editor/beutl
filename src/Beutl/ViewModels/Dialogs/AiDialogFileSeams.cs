namespace Beutl.ViewModels.Dialogs;

/// <summary>
/// The result of an AI dialog save picker. The picker and the file replacement
/// are kept as separate steps so identity fencing can reject a late destination
/// before it mutates that destination.
/// </summary>
internal sealed record AiSaveFileDestination(string Path);

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
            using (var stream = new FileStream(
                       temporaryPath,
                       new FileStreamOptions
                       {
                           Mode = FileMode.CreateNew,
                           Access = FileAccess.Write,
                           Share = FileShare.None,
                           BufferSize = 64 * 1024,
                           Options = FileOptions.WriteThrough,
                       }))
            {
                write(stream);
                cancellationToken.ThrowIfCancellationRequested();
                stream.Flush(true);
            }

            cancellationToken.ThrowIfCancellationRequested();
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
