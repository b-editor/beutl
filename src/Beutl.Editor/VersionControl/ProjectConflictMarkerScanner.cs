namespace Beutl.Editor.VersionControl;

internal static class ProjectConflictMarkerScanner
{
    private const int ScanChunkSize = 4096;

    private static readonly byte[] s_conflictMarkerBytes = "<<<<<<< "u8.ToArray();

    private static readonly HashSet<string> s_projectExtensions = new(
        [".bep", ".scene", ".belm"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> s_prunedDirectories = new(
        [
            ".beutl",
            ".git",
            ".idea",
            ".vs",
            "bin",
            "node_modules",
            "obj",
            "packages",
            "resources",
        ],
        StringComparer.OrdinalIgnoreCase);

    public static async Task<string?> FindFirstAsync(
        string projectFile,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFile);
        string projectRoot = Path.GetDirectoryName(Path.GetFullPath(projectFile))
                             ?? throw new ArgumentException(
                                 "The project file must have a parent directory.",
                                 nameof(projectFile));
        if (!Directory.Exists(projectRoot))
        {
            return null;
        }

        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(projectRoot);
        while (pendingDirectories.TryPop(out string? directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] files;
            string[] directories;
            try
            {
                files = Directory.GetFiles(directory);
                directories = Directory.GetDirectories(directory);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string childDirectory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ShouldDescendInto(childDirectory))
                {
                    pendingDirectories.Push(childDirectory);
                }
            }

            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!s_projectExtensions.Contains(Path.GetExtension(file)))
                {
                    continue;
                }

                if (!TryGetScannableLength(projectRoot, file, out long scanLength))
                {
                    continue;
                }

                try
                {
                    await using FileStream stream = new(
                        file,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete,
                        ScanChunkSize,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    if (await ContainsConflictMarkerAsync(
                            stream,
                            scanLength,
                            cancellationToken).ConfigureAwait(false))
                    {
                        return file;
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        return null;
    }

    private static bool TryGetScannableLength(
        string projectRoot,
        string file,
        out long scanLength)
    {
        scanLength = 0;
        try
        {
            var info = new FileInfo(file);
            info.Refresh();
            if (info.LinkTarget is not null
                || info.Length <= 0
                || !RepositoryPathComparer.IsContainedWithin(projectRoot, info.FullName))
            {
                return false;
            }

            scanLength = info.Length;
            return true;
        }
        catch (Exception ex)
            when (ex is IOException
                  or UnauthorizedAccessException
                  or NotSupportedException)
        {
            return false;
        }
    }

    private static async Task<bool> ContainsConflictMarkerAsync(
        Stream stream,
        long scanLength,
        CancellationToken cancellationToken)
    {
        int overlapCapacity = s_conflictMarkerBytes.Length - 1;
        byte[] buffer = new byte[ScanChunkSize + overlapCapacity];
        int overlapLength = 0;
        long remaining = scanLength;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int requested = (int)Math.Min(ScanChunkSize, remaining);
            int read = await stream.ReadAsync(
                buffer.AsMemory(overlapLength, requested),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            remaining -= read;
            int bufferedLength = overlapLength + read;
            if (buffer.AsSpan(0, bufferedLength).IndexOf(s_conflictMarkerBytes) >= 0)
            {
                return true;
            }

            overlapLength = Math.Min(overlapCapacity, bufferedLength);
            buffer.AsSpan(bufferedLength - overlapLength, overlapLength).CopyTo(buffer);
        }

        return false;
    }

    internal static bool ShouldDescendInto(string directory)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        if (s_prunedDirectories.Contains(name))
        {
            return false;
        }

        try
        {
            return new DirectoryInfo(directory).LinkTarget is null;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
