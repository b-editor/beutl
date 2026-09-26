using System.Diagnostics;

namespace Beutl.IO;

// Keep the destination intact until the writer has closed a complete output. The staging
// directory lives beside it so publication is a same-filesystem rename, including overwrites.
internal sealed class StagedOutputFile : IDisposable
{
    private readonly string _directory;

    public StagedOutputFile(string destinationPath)
    {
        DestinationPath = Path.GetFullPath(destinationPath);
        string parent = Path.GetDirectoryName(DestinationPath)!;
        if (!Directory.Exists(parent))
            throw new DirectoryNotFoundException(parent);

        _directory = Path.Combine(parent, $".beutl-output-{Guid.NewGuid():N}");
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(_directory);
        else
            Directory.CreateDirectory(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        // Preserve the filename/extension: encoders use them to select the container format.
        TemporaryPath = Path.Combine(_directory, Path.GetFileName(DestinationPath));
    }

    public string DestinationPath { get; }

    public string TemporaryPath { get; }

    public void Commit(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Move(TemporaryPath, DestinationPath, overwrite: true);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cleanup must not mask the original failure or turn a published output into a failure.
            Trace.TraceWarning("Failed to remove output staging directory {0}: {1}", _directory, ex.Message);
        }
    }
}
