namespace Beutl.Editor.VersionControl;

internal static class ProjectConflictMarkerScanner
{
    private const string ConflictMarker = "<<<<<<< ";

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

                try
                {
                    using var reader = new StreamReader(file);
                    while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                    {
                        if (line.Contains(ConflictMarker, StringComparison.Ordinal))
                        {
                            return file;
                        }
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

    internal static bool ShouldDescendInto(string directory)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        if (s_prunedDirectories.Contains(name))
        {
            return false;
        }

        try
        {
            return !File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint);
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
