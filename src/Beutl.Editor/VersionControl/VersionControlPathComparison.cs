using System.Text;

namespace Beutl.Editor.VersionControl;

/// <summary>Compares file paths after resolving their existing filesystem identity.</summary>
/// <remarks>
/// Existing components are rewritten to their on-disk spelling and symbolic links are followed.
/// Nonexistent suffixes retain their lexical spelling. Results describe a point-in-time lookup and
/// do not lock the inspected entries against concurrent replacement. If several non-exact
/// case/normalization candidates are visible, the supplied spelling is retained rather than
/// guessing which distinct entry the filesystem resolved.
/// </remarks>
internal static class VersionControlPathComparison
{
    private const int MaxSymbolicLinkHops = 64;

    /// <summary>Determines whether two paths have the same canonical spelling.</summary>
    public static bool AreSameCanonicalPath(string left, string right)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(left);
        ArgumentException.ThrowIfNullOrWhiteSpace(right);

        return string.Equals(
            ResolveCanonicalPath(left),
            ResolveCanonicalPath(right),
            StringComparison.Ordinal);
    }

    /// <summary>Determines whether a path is the root itself or one of its descendants.</summary>
    public static bool IsSameOrDescendant(string root, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string canonicalRoot = Path.TrimEndingDirectorySeparator(ResolveCanonicalPath(root));
        string canonicalPath = Path.TrimEndingDirectorySeparator(ResolveCanonicalPath(path));
        if (string.Equals(canonicalRoot, canonicalPath, StringComparison.Ordinal))
        {
            return true;
        }

        string prefix = canonicalRoot.EndsWith(Path.DirectorySeparatorChar)
            ? canonicalRoot
            : canonicalRoot + Path.DirectorySeparatorChar;
        return canonicalPath.StartsWith(prefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tries to determine whether two single-component child paths identify the same canonical path.
    /// </summary>
    /// <remarks>
    /// The method returns <see langword="false"/> when case- or normalization-variant children are
    /// both absent or when their filesystem metadata cannot be inspected. A successful result is
    /// only a snapshot; callers that mutate either path still need an operation-specific ownership
    /// mechanism.
    /// </remarks>
    public static bool TryAreSameChildPath(
        string parentPath,
        string leftName,
        string rightName,
        out bool areSame)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentPath);
        ValidateChildName(leftName, nameof(leftName));
        ValidateChildName(rightName, nameof(rightName));

        areSame = false;
        if (string.Equals(leftName, rightName, StringComparison.Ordinal))
        {
            areSame = true;
            return true;
        }

        if (!CouldHaveEquivalentFilesystemSpelling(leftName, rightName))
        {
            return true;
        }

        string leftPath = Path.Combine(parentPath, leftName);
        string rightPath = Path.Combine(parentPath, rightName);
        bool leftExists = Path.Exists(leftPath);
        bool rightExists = Path.Exists(rightPath);
        if (!leftExists || !rightExists)
        {
            return leftExists != rightExists;
        }

        try
        {
            areSame = AreSameCanonicalPath(leftPath, rightPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Resolves symbolic links and the on-disk spelling of each existing path component.
    /// </summary>
    /// <remarks>Any nonexistent suffix is retained exactly as supplied.</remarks>
    public static string ResolveCanonicalPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        string root = NormalizeRoot(
            Path.GetPathRoot(fullPath)
            ?? throw new IOException($"The path '{path}' has no root."));
        var components = new Queue<string>(SplitComponents(fullPath, root));
        var visitedStates = new HashSet<string>(StringComparer.Ordinal)
        {
            CreateResolutionState(root, components),
        };
        string resolved = root;
        int linkHops = 0;

        while (components.TryDequeue(out string? component))
        {
            if (component == ".")
            {
                continue;
            }

            if (component == "..")
            {
                resolved = Path.GetDirectoryName(resolved) ?? resolved;
                continue;
            }

            string candidate = Path.GetFullPath(Path.Combine(resolved, component));
            candidate = NormalizeExistingEntrySpelling(resolved, component, candidate);
            string? target = TryGetLinkTarget(candidate);
            if (target is null)
            {
                resolved = candidate;
                continue;
            }

            linkHops++;
            if (linkHops > MaxSymbolicLinkHops)
            {
                throw new IOException(
                    $"The path '{path}' exceeds the symbolic-link resolution limit.");
            }

            string targetRoot = NormalizeRoot(Path.GetPathRoot(target) ?? string.Empty);
            if (Path.IsPathFullyQualified(target))
            {
                resolved = targetRoot;
            }
            else if (Path.IsPathRooted(target))
            {
                if (!OperatingSystem.IsWindows()
                    || targetRoot.Length != 1
                    || targetRoot[0] is not ('\\' or '/'))
                {
                    throw new IOException(
                        $"The symbolic link '{candidate}' has an unsupported rooted target.");
                }

                resolved = NormalizeRoot(
                    Path.GetPathRoot(resolved)
                    ?? throw new IOException(
                        $"The path '{path}' has no drive root."));
            }

            IEnumerable<string> targetComponents = SplitComponents(target, targetRoot);
            components = new Queue<string>(targetComponents.Concat(components));
            string state = CreateResolutionState(resolved, components);
            if (!visitedStates.Add(state))
            {
                throw new IOException(
                    $"A symbolic-link cycle was found while resolving '{path}'.");
            }
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(resolved));
    }

    private static void ValidateChildName(string name, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, paramName);
        if (name is "." or ".."
            || Path.IsPathRooted(name)
            || name.AsSpan().IndexOfAny(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) >= 0
            || OperatingSystem.IsWindows() && name.Contains(Path.VolumeSeparatorChar))
        {
            throw new ArgumentException(
                "A child name must contain exactly one path component.",
                paramName);
        }
    }

    private static string NormalizeRoot(string root)
    {
        return OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root;
    }

    private static IEnumerable<string> SplitComponents(string path, string root)
    {
        return path[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
    }

    private static string CreateResolutionState(string resolved, IEnumerable<string> components)
    {
        return string.Join('\0', new[] { resolved }.Concat(components));
    }

    private static string? TryGetLinkTarget(string path)
    {
        FileSystemInfo info = Directory.Exists(path)
            ? new DirectoryInfo(path)
            : new FileInfo(path);
        try
        {
            return info.LinkTarget;
        }
        catch (Exception ex)
            when (ex is IOException
                  or UnauthorizedAccessException
                  or NotSupportedException
                  or ArgumentException)
        {
            throw new IOException(
                $"Could not inspect symbolic-link metadata for '{path}'.",
                ex);
        }
    }

    private static bool CouldHaveEquivalentFilesystemSpelling(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   left.Normalize(NormalizationForm.FormC),
                   right.Normalize(NormalizationForm.FormC),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeExistingEntrySpelling(
        string parent,
        string component,
        string candidate)
    {
        if (!Path.Exists(candidate))
        {
            return candidate;
        }

        try
        {
            return SelectCanonicalExistingEntry(
                component,
                candidate,
                Directory.EnumerateFileSystemEntries(parent));
        }
        catch (Exception ex)
            when (ex is IOException
                  or UnauthorizedAccessException
                  or NotSupportedException)
        {
            throw new IOException(
                $"Could not normalize the on-disk spelling of '{candidate}'.",
                ex);
        }
    }

    internal static string SelectCanonicalExistingEntry(
        string component,
        string candidate,
        IEnumerable<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        string normalizedComponent = component.Normalize(NormalizationForm.FormC);
        string? equivalentMatch = null;
        bool equivalentMatchAmbiguous = false;

        foreach (string entry in entries)
        {
            string entryName = Path.GetFileName(entry);
            if (string.Equals(entryName, component, StringComparison.Ordinal))
            {
                return entry;
            }

            if (string.Equals(entryName, component, StringComparison.OrdinalIgnoreCase))
            {
                TrackUniqueMatch(
                    entry,
                    ref equivalentMatch,
                    ref equivalentMatchAmbiguous);
                continue;
            }

            string normalizedEntryName = entryName.Normalize(NormalizationForm.FormC);
            if (string.Equals(
                    normalizedEntryName,
                    normalizedComponent,
                    StringComparison.Ordinal))
            {
                TrackUniqueMatch(
                    entry,
                    ref equivalentMatch,
                    ref equivalentMatchAmbiguous);
            }
            else if (string.Equals(
                         normalizedEntryName,
                         normalizedComponent,
                         StringComparison.OrdinalIgnoreCase))
            {
                TrackUniqueMatch(
                    entry,
                    ref equivalentMatch,
                    ref equivalentMatchAmbiguous);
            }
        }

        return equivalentMatchAmbiguous ? candidate : equivalentMatch ?? candidate;
    }

    private static void TrackUniqueMatch(
        string entry,
        ref string? match,
        ref bool ambiguous)
    {
        if (match is null)
        {
            match = entry;
        }
        else if (!string.Equals(match, entry, StringComparison.Ordinal))
        {
            ambiguous = true;
        }
    }

}
