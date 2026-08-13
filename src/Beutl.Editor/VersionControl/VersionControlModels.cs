using System.Text.RegularExpressions;

namespace Beutl.Editor.VersionControl;

internal static class RepositoryPathComparer
{
    private const int MaxSymbolicLinkHops = 64;

    // Both comparisons below run on canonical paths, where ResolveCanonicalPath has already folded
    // the casing the filesystem itself merges. Ordinal is therefore exact in both directions: two
    // spellings of one directory have converged, and two directories that only a case-insensitive
    // rule would merge stay apart on a case-sensitive volume.
    internal static bool AreEquivalent(string left, string right)
    {
        return string.Equals(
            ResolveCanonicalPath(left),
            ResolveCanonicalPath(right),
            StringComparison.Ordinal);
    }

    internal static bool IsContainedWithin(string root, string path)
    {
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

    internal static string ResolveCanonicalPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath)
                      ?? throw new IOException($"The path '{path}' has no root.");
        var components = new Queue<string>(SplitComponents(fullPath, root));
        var visitedStates = new HashSet<string>(PathComparer)
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
            candidate = NormalizeExistingEntryCasing(resolved, component, candidate);
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

            string targetRoot = Path.GetPathRoot(target) ?? string.Empty;
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

                resolved = Path.GetPathRoot(resolved)
                           ?? throw new IOException(
                               $"The path '{path}' has no drive root.");
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

    private static string NormalizeExistingEntryCasing(
        string parent,
        string component,
        string candidate)
    {
        if (!OperatingSystem.IsMacOS() || !Path.Exists(candidate))
        {
            return candidate;
        }

        try
        {
            string? insensitiveMatch = null;
            foreach (string entry in Directory.EnumerateFileSystemEntries(parent))
            {
                string entryName = Path.GetFileName(entry);
                if (string.Equals(entryName, component, StringComparison.Ordinal))
                {
                    return entry;
                }

                if (insensitiveMatch is null
                    && string.Equals(entryName, component, StringComparison.OrdinalIgnoreCase))
                {
                    insensitiveMatch = entry;
                }
            }

            return insensitiveMatch ?? candidate;
        }
        catch (Exception ex)
            when (ex is IOException
                  or UnauthorizedAccessException
                  or NotSupportedException)
        {
            throw new IOException(
                $"Could not normalize the on-disk casing of '{candidate}'.",
                ex);
        }
    }

    private static StringComparer PathComparer
        => FileSystemPathComparison.ComparerForCurrentPlatform;
}

public enum GitAvailabilityState
{
    Installed,
    NotInstalled,
    VersionTooOld,
}

public sealed record GitAvailability(
    GitAvailabilityState State,
    string? GitPath,
    Version? Version,
    bool LfsInstalled)
{
    public static GitAvailability NotInstalled { get; } = new(
        GitAvailabilityState.NotInstalled,
        GitPath: null,
        Version: null,
        LfsInstalled: false);
}

public sealed record RepositoryInfo
{
    private readonly string _canonicalRepoRoot;
    private readonly string _canonicalProjectRoot;

    public RepositoryInfo(string repoRoot, string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        string normalizedRepoRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoRoot));
        string normalizedProjectRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));

        // Containment and identity are decided on canonical paths compared ordinally.
        // ResolveCanonicalPath follows symbolic links and rewrites each existing component to its
        // on-disk casing, so two spellings of one directory converge while two directories that a
        // case-insensitive rule would merge stay apart on a case-sensitive volume. Comparing the
        // given paths under a per-platform rule gets one of those two cases wrong either way.
        string canonicalRepoRoot = Path.TrimEndingDirectorySeparator(
            RepositoryPathComparer.ResolveCanonicalPath(normalizedRepoRoot));
        string canonicalProjectRoot = Path.TrimEndingDirectorySeparator(
            RepositoryPathComparer.ResolveCanonicalPath(normalizedProjectRoot));
        string relativeProject = GetContainedRelativePath(canonicalRepoRoot, canonicalProjectRoot)
                                 ?? throw new ArgumentException(
                                     "The project root must be inside the repository root.",
                                     nameof(projectRoot));
        bool nested = relativeProject != ".";

        RepoRoot = normalizedRepoRoot;
        ProjectRoot = normalizedProjectRoot;
        _canonicalRepoRoot = canonicalRepoRoot;
        _canonicalProjectRoot = canonicalProjectRoot;
        IsNestedInForeignRepo = nested;
        Pathspec = nested ? NormalizePathspec(relativeProject) : ".";
    }

    // Returns null when path is not inside root. Both are fully qualified and trimmed, so an
    // ordinal prefix test is exact; Path.GetRelativePath cannot be used because it applies the
    // per-platform casing rule this type deliberately avoids.
    private static string? GetContainedRelativePath(string root, string path)
    {
        if (string.Equals(root, path, StringComparison.Ordinal))
        {
            return ".";
        }

        string prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.Ordinal)
            ? path[prefix.Length..]
            : null;
    }

    private static string NormalizePathspec(string path)
        => OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;

    public string RepoRoot { get; }

    public string ProjectRoot { get; }

    public bool IsNestedInForeignRepo { get; }

    public string Pathspec { get; }

    public bool Equals(RepositoryInfo? other)
    {
        return other is not null
               && string.Equals(_canonicalRepoRoot, other._canonicalRepoRoot, StringComparison.Ordinal)
               && string.Equals(_canonicalProjectRoot, other._canonicalProjectRoot, StringComparison.Ordinal);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(_canonicalRepoRoot),
            StringComparer.Ordinal.GetHashCode(_canonicalProjectRoot));
    }
}

public enum SnapshotKind
{
    Manual,
    Save,
    Close,
    Safety,
    Restore,
    Recovery,
    Init,
}

internal sealed record CheckedOutBranchTip(string RefName, string Commit);

internal enum PullTransitionState
{
    Unchanged,
    Applied,
    OwnershipLost,
    RecoveryFailed,
}

internal sealed record PullPreflightResult(
    RemoteOpResult Result,
    bool RequiresTransition);

internal sealed record FastForwardPullResult(
    RemoteOpResult Result,
    CheckedOutBranchTip Tip,
    PullTransitionState TransitionState = PullTransitionState.Unchanged,
    CheckedOutBranchTip? TargetTip = null,
    PendingPullRecovery? Recovery = null);

internal sealed record ProjectCheckpoint(
    string RefName,
    string Commit,
    CheckedOutBranchTip BaseTip);

internal sealed record PendingPullRecovery(
    string Id,
    string DescriptorRef,
    string DescriptorObject,
    ProjectCheckpoint Checkpoint,
    CheckedOutBranchTip TargetTip,
    string ProjectFile,
    DateTimeOffset CreatedAt)
{
    public string RecoveryBranchName => $"beutl/recovery/{Id}";
}

internal enum PendingPullRecoveryOutcome
{
    RestoredOriginal,
    ReappliedCheckpoint,
}

internal sealed class PendingPullRecoveryPreservedException : Exception
{
    public PendingPullRecoveryPreservedException(string recoveryReference, Exception? inner = null)
        : base(
            $"The checkpoint remains available at Git reference '{recoveryReference}', but the worktree could not be changed safely.",
            inner)
    {
        RecoveryReference = recoveryReference;
    }

    public string RecoveryReference { get; }
}

public sealed record ProjectRecoveryInfo(
    string Id,
    string ProjectFileName,
    DateTimeOffset CreatedAt);

public abstract record ProjectRecoveryResult
{
    private ProjectRecoveryResult()
    {
    }

    public sealed record RestoredOriginal : ProjectRecoveryResult;

    public sealed record ReappliedCheckpoint(string RecoveryBranchName) : ProjectRecoveryResult;

    public sealed record Declined : ProjectRecoveryResult;

    public sealed record NotFoundOrChanged : ProjectRecoveryResult;

    public sealed record Unavailable : ProjectRecoveryResult;

    public sealed record FailedPreserved(string RecoveryReference) : ProjectRecoveryResult;

    public sealed record FailedUncertain : ProjectRecoveryResult;
}

internal abstract record BranchTipRollbackResult
{
    private BranchTipRollbackResult()
    {
    }

    public sealed record RolledBack : BranchTipRollbackResult;

    public sealed record RefChanged(string? ActualCommit) : BranchTipRollbackResult;

    public sealed record UnsafeRepositoryState : BranchTipRollbackResult;
}

public sealed record CommitInfo(
    string Sha,
    string ShortSha,
    string Subject,
    string AuthorName,
    DateTimeOffset AuthorDate,
    SnapshotKind Kind);

public enum FileChangeStatus
{
    Added,
    Modified,
    Deleted,
    Renamed,
}

public sealed record FileChange(
    string Path,
    FileChangeStatus Status,
    string? OldPath = null);

public sealed record WorkspaceStatus(
    string? Branch,
    int Ahead,
    int Behind,
    IReadOnlyList<FileChange> Changes,
    bool HasConflicts)
{
    public bool IsClean => Changes.Count == 0;
}

/// <summary>
/// Describes whether the revision created by a successful commit could be observed.
/// </summary>
public abstract record CommitRevision
{
    private CommitRevision()
    {
    }

    /// <summary>
    /// The successful commit revision was observed.
    /// </summary>
    public sealed record Known : CommitRevision
    {
        /// <summary>
        /// Creates an observed commit revision.
        /// </summary>
        /// <param name="sha">The non-empty Git object name.</param>
        public Known(string sha)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sha);
            Sha = sha;
        }

        /// <summary>
        /// Gets the observed Git object name.
        /// </summary>
        public string Sha { get; }
    }

    /// <summary>
    /// The commit succeeded, but its revision could not be observed afterward.
    /// </summary>
    public sealed record Unavailable : CommitRevision;
}

public abstract record CommitResult
{
    private CommitResult()
    {
    }

    public sealed record NoChanges : CommitResult;

    /// <summary>
    /// A durable commit succeeded. <see cref="Revision"/> records whether its SHA was observed.
    /// </summary>
    public sealed record Committed : CommitResult
    {
        /// <summary>
        /// Creates a durable commit result with its revision observation state.
        /// </summary>
        /// <param name="revision">The non-null revision observation state.</param>
        public Committed(CommitRevision revision)
        {
            Revision = revision ?? throw new ArgumentNullException(nameof(revision));
        }

        /// <summary>
        /// Gets the revision observation state for the successful commit.
        /// </summary>
        public CommitRevision Revision { get; }
    }

    public sealed record SkippedNoIdentity : CommitResult;
}

public abstract record RemoteOpResult
{
    private RemoteOpResult()
    {
    }

    public sealed record Success : RemoteOpResult;

    public sealed record AuthFailed(string Guidance) : RemoteOpResult;

    public sealed record Diverged : RemoteOpResult;

    public sealed record Offline : RemoteOpResult;

    public sealed record RepositoryDirty : RemoteOpResult;

    public sealed record Failed(string Stderr) : RemoteOpResult;
}

public sealed record BranchInfo(string Name, bool IsCurrent, string? UpstreamName);

public sealed record RemoteInfo(string Name, string Url);

public sealed record GitIdentity(string Name, string Email);

internal abstract record VersionControlPolicyNotice
{
    private VersionControlPolicyNotice()
    {
    }

    internal sealed record LfsRemoteQuota : VersionControlPolicyNotice;

    internal sealed record LargeMediaWithoutLfs(
        string Path,
        long SizeBytes) : VersionControlPolicyNotice;

    internal sealed record MissingIdentity : VersionControlPolicyNotice;
}

public sealed record RepositoryLockInfo(
    string LockPath,
    DateTimeOffset LastWriteTimeUtc);

internal sealed record InitOptions(
    RepositoryInfo TargetRepository,
    bool UseLfsWhenAvailable = true)
{
    public GitIdentity? Identity { get; init; }
}

internal static partial class GitDiagnosticSanitizer
{
    internal static string RedactCredentials(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        string redacted = CredentialUrlRegex().Replace(value, "${scheme}***@");
        return UrlQueryOrFragmentRegex().Replace(
            redacted,
            "${url}${separator}***");
    }

    [GeneratedRegex(
        @"(?<scheme>[a-z][a-z0-9+.-]*://)[^/\s@]+@",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialUrlRegex();

    [GeneratedRegex(
        @"(?<url>[a-z][a-z0-9+.-]*://[^\s?#'\""<>]*)(?<separator>[?#])[^\s'\""<>]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlQueryOrFragmentRegex();
}

public sealed class GitOperationException : Exception
{
    public GitOperationException(int exitCode, string stderr)
        : base(CreateMessage(exitCode, GitDiagnosticSanitizer.RedactCredentials(stderr)))
    {
        ExitCode = exitCode;
        Stderr = GitDiagnosticSanitizer.RedactCredentials(stderr);
    }

    public int ExitCode { get; }

    public string Stderr { get; }

    public bool IsRepositoryLockFailure
        => Stderr.Contains("another git process seems to be running", StringComparison.OrdinalIgnoreCase)
           || Stderr.Contains("index.lock", StringComparison.OrdinalIgnoreCase)
           || Stderr.Contains("HEAD.lock", StringComparison.OrdinalIgnoreCase)
           || Stderr.Contains("config.lock", StringComparison.OrdinalIgnoreCase)
           || Stderr.Contains("could not lock config file", StringComparison.OrdinalIgnoreCase);

    private static string CreateMessage(int exitCode, string stderr)
    {
        return string.IsNullOrEmpty(stderr)
            ? $"Git exited with code {exitCode}."
            : $"Git exited with code {exitCode}: {stderr}";
    }

}

public sealed class GitIdentityRequiredException : InvalidOperationException
{
    public GitIdentityRequiredException()
        : base("A Git user name and email address are required to create this commit.")
    {
    }
}

public sealed class EnclosingRepositoryConsentRequiredException : InvalidOperationException
{
    public EnclosingRepositoryConsentRequiredException(RepositoryInfo repository)
        : base(
            $"The project is inside the Git repository at '{repository.RepoRoot}'. "
            + "Explicit consent is required before Beutl can use that repository.")
    {
        if (!repository.IsNestedInForeignRepo)
        {
            throw new ArgumentException(
                "The repository must enclose the project root.",
                nameof(repository));
        }

        Repository = repository;
    }

    public RepositoryInfo Repository { get; }
}

public sealed class VersionControlConflictedException : InvalidOperationException
{
    public VersionControlConflictedException(string guidance)
        : base(guidance)
    {
        Guidance = guidance;
    }

    public string Guidance { get; }
}

internal sealed class DetachedHeadNotSupportedException : InvalidOperationException
{
    public DetachedHeadNotSupportedException()
        : base("This operation requires a checked-out local branch; detached HEAD is not supported.")
    {
    }
}

internal sealed class ProjectCheckpointChangedException : InvalidOperationException
{
    public ProjectCheckpointChangedException(string refName)
        : base($"The project checkpoint ref '{refName}' changed outside Beutl.")
    {
        RefName = refName;
    }

    public string RefName { get; }
}

internal sealed class ProjectCheckpointStateChangedException : InvalidOperationException
{
    public ProjectCheckpointStateChangedException()
        : base("The project changed after its safety checkpoint was created.")
    {
    }
}

internal sealed class ProjectCheckpointStagedChangesException : InvalidOperationException
{
    public ProjectCheckpointStagedChangesException()
        : base(
            "A safety checkpoint cannot be created while the project contains staged changes.")
    {
    }
}

internal sealed class PendingPullRecoveryChangedException : InvalidOperationException
{
    public PendingPullRecoveryChangedException(string refName)
        : base($"The pending pull recovery ref '{refName}' changed outside Beutl.")
    {
        RefName = refName;
    }

    public PendingPullRecoveryChangedException(string refName, Exception innerException)
        : base(
            $"The pending pull recovery ref '{refName}' changed outside Beutl.",
            innerException)
    {
        RefName = refName;
    }

    public string RefName { get; }
}
