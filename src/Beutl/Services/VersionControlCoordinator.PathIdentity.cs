using Beutl.Editor.VersionControl;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private static bool PathsEqual(string left, string right)
    {
        return VersionControlPathComparison.AreSameCanonicalPath(left, right);
    }

    private static bool NullablePathsEqual(string? left, string? right)
    {
        return left is null || right is null
            ? left is null && right is null
            : PathsEqual(left, right);
    }

    private static bool RepositoriesEqual(RepositoryInfo left, RepositoryInfo right)
    {
        return left.IsNestedInForeignRepo == right.IsNestedInForeignRepo
               && PathsEqual(left.RepoRoot, right.RepoRoot)
               && PathsEqual(left.ProjectRoot, right.ProjectRoot);
    }

    private static bool RecoveryProjectPathsEqual(string left, string right)
    {
        string lexicalLeft = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
        string lexicalRight = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
        if (string.Equals(lexicalLeft, lexicalRight, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            return PathsEqual(left, right);
        }
        catch (Exception ex)
            when (ex is IOException
                  or UnauthorizedAccessException
                  or NotSupportedException
                  or ArgumentException)
        {
            return false;
        }
    }

    private static bool RecoveryProjectPathsEqual(
        RepositoryInfo repository,
        string left,
        string right)
    {
        if (TryGetRecoveryRelativePath(repository, left, out string? leftRelative)
            && TryGetRecoveryRelativePath(repository, right, out string? rightRelative)
            && string.Equals(leftRelative, rightRelative, StringComparison.Ordinal))
        {
            return true;
        }

        return RecoveryProjectPathsEqual(left, right);
    }

    private static bool TryGetRecoveryRelativePath(
        RepositoryInfo repository,
        string path,
        out string? relativePath)
    {
        string fullPath = Path.GetFullPath(path);
        string? ancestor = Path.GetDirectoryName(fullPath);
        while (ancestor is not null)
        {
            try
            {
                if (VersionControlPathComparison.AreSameCanonicalPath(
                        ancestor,
                        repository.ProjectRoot))
                {
                    relativePath = Path.GetRelativePath(ancestor, fullPath);
                    return relativePath != ".."
                           && !relativePath.StartsWith(
                               $"..{Path.DirectorySeparatorChar}",
                               StringComparison.Ordinal)
                           && !Path.IsPathRooted(relativePath);
                }
            }
            catch (Exception ex)
                when (ex is IOException
                      or UnauthorizedAccessException
                      or NotSupportedException
                      or ArgumentException)
            {
                // A mutated child link must not prevent finding a safe lexical root ancestor.
            }

            ancestor = Path.GetDirectoryName(ancestor);
        }

        relativePath = null;
        return false;
    }

    private static void EnsureProjectFileIsPhysicallyContained(
        RepositoryInfo repository,
        string projectFile)
    {
        EnsureProjectFileIsPhysicallyContained(repository.ProjectRoot, projectFile);
    }

    private static void EnsureProjectFileIsPhysicallyContained(
        string projectRoot,
        string projectFile)
    {
        if (!IsProjectFileContained(projectRoot, projectFile))
        {
            throw new InvalidOperationException(
                $"The project file '{projectFile}' resolves outside the version-controlled project root.");
        }
    }

    private static bool IsProjectFileContained(string projectRoot, string projectFile)
    {
        try
        {
            return VersionControlPathComparison.IsSameOrDescendant(projectRoot, projectFile);
        }
        catch (Exception ex)
            when (ex is IOException
                  or UnauthorizedAccessException
                  or NotSupportedException
                  or ArgumentException)
        {
            // Only when the path cannot be canonicalized at all - a symbolic-link cycle, or a
            // component that cannot be read. A resolvable path that lands outside has already
            // returned false above, so this fallback cannot turn an escape into containment.
        }

        string fullPath = Path.GetFullPath(projectFile);
        string? ancestor = Path.GetDirectoryName(fullPath);
        while (ancestor is not null)
        {
            try
            {
                if (VersionControlPathComparison.AreSameCanonicalPath(ancestor, projectRoot))
                {
                    string relative = Path.GetRelativePath(ancestor, fullPath);
                    return relative != ".."
                           && !relative.StartsWith(
                               $"..{Path.DirectorySeparatorChar}",
                               StringComparison.Ordinal)
                           && !Path.IsPathRooted(relative);
                }
            }
            catch (Exception ex)
                when (ex is IOException
                      or UnauthorizedAccessException
                      or NotSupportedException
                      or ArgumentException)
            {
                // An unresolvable child must not stop the walk from reaching a resolvable ancestor.
            }

            ancestor = Path.GetDirectoryName(ancestor);
        }

        return false;
    }
}
