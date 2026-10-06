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
        catch (Exception ex) when (IsPathResolutionFailure(ex))
        {
            return false;
        }
    }

    private static bool RecoveryProjectPathsEqual(
        RepositoryInfo repository,
        string left,
        string right)
    {
        if (TryGetPathRelativeToRoot(repository.ProjectRoot, left, out string? leftRelative)
            && TryGetPathRelativeToRoot(repository.ProjectRoot, right, out string? rightRelative)
            && string.Equals(leftRelative, rightRelative, StringComparison.Ordinal))
        {
            return true;
        }

        return RecoveryProjectPathsEqual(left, right);
    }

    // Walks up from the path to the ancestor that is the root, and reports whether the path stays
    // inside it.
    private static bool TryGetPathRelativeToRoot(
        string root,
        string path,
        out string? relativePath)
    {
        string fullPath = Path.GetFullPath(path);
        string? ancestor = Path.GetDirectoryName(fullPath);
        while (ancestor is not null)
        {
            try
            {
                if (VersionControlPathComparison.AreSameCanonicalPath(ancestor, root))
                {
                    relativePath = Path.GetRelativePath(ancestor, fullPath);
                    return relativePath != ".."
                           && !relativePath.StartsWith(
                               $"..{Path.DirectorySeparatorChar}",
                               StringComparison.Ordinal)
                           && !Path.IsPathRooted(relativePath);
                }
            }
            catch (Exception ex) when (IsPathResolutionFailure(ex))
            {
                // An unresolvable or mutated child link must not stop the walk from reaching a
                // resolvable root ancestor.
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
        catch (Exception ex) when (IsPathResolutionFailure(ex))
        {
            // Only when the path cannot be canonicalized at all - a symbolic-link cycle, or a
            // component that cannot be read. A resolvable path that lands outside has already
            // returned false above, so this fallback cannot turn an escape into containment.
        }

        return TryGetPathRelativeToRoot(projectRoot, projectFile, out _);
    }

    // A path that does not exist, cannot be read or is malformed, as opposed to a failure that has to
    // surface.
    private static bool IsPathResolutionFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException;
    }
}
