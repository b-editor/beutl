using System.Diagnostics;

namespace Beutl.IO;

// Keep the destination intact until the writer has closed a complete output. The staging
// directory lives beside it so publication is a same-filesystem rename, including overwrites.
internal sealed class StagedOutputFile : IDisposable
{
    private readonly string _directory;

    public StagedOutputFile(string destinationPath)
    {
        string requestedPath = Path.GetFullPath(destinationPath);
        var destination = new FileInfo(requestedPath);
        DestinationPath = destination.LinkTarget is null
            ? requestedPath
            : destination.ResolveLinkTarget(returnFinalTarget: true)!.FullName;
        string parent = Path.GetDirectoryName(DestinationPath)!;
        if (!Directory.Exists(parent))
            throw new DirectoryNotFoundException(parent);

        _directory = Path.Combine(parent, $".beutl-output-{Guid.NewGuid():N}");
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(_directory);
        else
            Directory.CreateDirectory(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        // Preserve the filename/extension: encoders use them to select the container format.
        TemporaryPath = Path.Combine(_directory, Path.GetFileName(requestedPath));
    }

    public string DestinationPath { get; }

    public string TemporaryPath { get; }

    public void Commit(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() && File.Exists(DestinationPath))
        {
            // Read at publication time: permissions can change while an encoder is running.
            // A failure to preserve them must leave the previous output intact.
            File.SetUnixFileMode(TemporaryPath, File.GetUnixFileMode(DestinationPath));
        }

        File.Move(TemporaryPath, DestinationPath, overwrite: true);
    }

    /// <summary>
    /// Publishes files written directly in the staging directory, each beside the destination under its own
    /// name, and returns where they went. <see cref="TemporaryPath"/> goes to <see cref="DestinationPath"/>.
    /// </summary>
    /// <remarks>
    /// Every file is checked before the first one moves, and a failure part way puts back what was already
    /// replaced, so the destination folder ends up with all of the files or none.
    /// </remarks>
    public IReadOnlyList<string> Commit(IReadOnlyList<string> files, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        (string Source, string Destination)[] moves = [.. files.Select(GetMove)];
        if (moves is [{ Source: var only }] && only == TemporaryPath)
        {
            Commit(cancellationToken);
            return [DestinationPath];
        }

        string backups = Path.Combine(_directory, $".previous-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backups);
        var started = new Stack<(string Destination, string? Backup)>();
        try
        {
            foreach ((string source, string destination) in moves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? backup = null;
                var existing = new FileInfo(destination);
                if (existing.Exists || existing.LinkTarget != null)
                {
                    if (!OperatingSystem.IsWindows() && existing.LinkTarget == null)
                        File.SetUnixFileMode(source, File.GetUnixFileMode(destination));

                    backup = Path.Combine(backups, Path.GetFileName(source));
                    File.Move(destination, backup);
                }

                started.Push((destination, backup));
                File.Move(source, destination);
            }
        }
        catch
        {
            foreach ((string destination, string? backup) in started)
                Restore(destination, backup);

            throw;
        }

        return [.. moves.Select(move => move.Destination)];
    }

    private (string Source, string Destination) GetMove(string file)
    {
        string source = Path.GetFullPath(file);
        if (Path.GetDirectoryName(source) != _directory)
            throw new InvalidOperationException($"'{file}' is not in the output's staging directory, so it cannot be published.");
        if (!File.Exists(source))
            throw new FileNotFoundException("An output file was not written.", source);

        // Only the path the user chose follows a link. The others are names an encoder made up, so one that
        // happens to be a link is replaced rather than written through to wherever it points.
        return (source, source == TemporaryPath
            ? DestinationPath
            : Path.Combine(Path.GetDirectoryName(DestinationPath)!, Path.GetFileName(source)));
    }

    private static void Restore(string destination, string? backup)
    {
        try
        {
            var published = new FileInfo(destination);
            if (published.Exists || published.LinkTarget != null)
                published.Delete();
            if (backup != null)
                File.Move(backup, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The publication failure is what the caller has to hear about.
            Trace.TraceWarning("Failed to restore output {0}: {1}", destination, ex.Message);
        }
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
