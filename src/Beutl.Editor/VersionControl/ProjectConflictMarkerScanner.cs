namespace Beutl.Editor.VersionControl;

internal static class ProjectConflictMarkerScanner
{
    private const string ConflictMarker = "<<<<<<< ";

    private static readonly HashSet<string> s_projectExtensions = new(
        [".bep", ".scene", ".belm"],
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

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
        };
        foreach (string file in Directory.EnumerateFiles(projectRoot, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!s_projectExtensions.Contains(Path.GetExtension(file))
                || IsInsideGitDirectory(projectRoot, file))
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

        return null;
    }

    private static bool IsInsideGitDirectory(string projectRoot, string file)
    {
        string relativePath = Path.GetRelativePath(projectRoot, file);
        return relativePath.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Contains(".git", StringComparer.OrdinalIgnoreCase);
    }
}
