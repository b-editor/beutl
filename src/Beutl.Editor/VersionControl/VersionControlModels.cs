using System.Text.RegularExpressions;

namespace Beutl.Editor.VersionControl;

internal static class RepositoryPathComparer
{
    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal static bool AreEquivalent(string left, string right)
    {
        return string.Equals(
            ResolveExistingDirectoryPath(left),
            ResolveExistingDirectoryPath(right),
            PathComparison);
    }

    private static string ResolveExistingDirectoryPath(string path)
    {
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string root = Path.GetPathRoot(fullPath) ?? fullPath;
        var components = new Stack<string>();
        string? current = fullPath;
        while (current is not null
               && !string.Equals(current, root, PathComparison))
        {
            string name = Path.GetFileName(current);
            if (!string.IsNullOrEmpty(name))
            {
                components.Push(name);
            }

            current = Path.GetDirectoryName(current);
        }

        string resolved = root;
        while (components.Count > 0)
        {
            string candidate = Path.Combine(resolved, components.Pop());
            try
            {
                FileSystemInfo? target = new DirectoryInfo(candidate)
                    .ResolveLinkTarget(returnFinalTarget: true);
                resolved = target is null
                    ? candidate
                    : ResolveExistingDirectoryPath(target.FullName);
            }
            catch (Exception ex)
                when (ex is IOException
                      or UnauthorizedAccessException
                      or NotSupportedException)
            {
                resolved = candidate;
            }
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(resolved));
    }
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
    public RepositoryInfo(string repoRoot, string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        string normalizedRepoRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoRoot));
        string normalizedProjectRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        string relativeProject = Path.GetRelativePath(normalizedRepoRoot, normalizedProjectRoot);
        if (relativeProject == ".."
            || relativeProject.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(relativeProject))
        {
            throw new ArgumentException("The project root must be inside the repository root.", nameof(projectRoot));
        }

        bool nested = !string.Equals(normalizedRepoRoot, normalizedProjectRoot, PathComparison);

        RepoRoot = normalizedRepoRoot;
        ProjectRoot = normalizedProjectRoot;
        IsNestedInForeignRepo = nested;
        Pathspec = nested ? relativeProject.Replace('\\', '/') : ".";
    }

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer PathComparer
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public string RepoRoot { get; }

    public string ProjectRoot { get; }

    public bool IsNestedInForeignRepo { get; }

    public string Pathspec { get; }

    public bool Equals(RepositoryInfo? other)
    {
        return other is not null
               && PathComparer.Equals(RepoRoot, other.RepoRoot)
               && PathComparer.Equals(ProjectRoot, other.ProjectRoot);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            PathComparer.GetHashCode(RepoRoot),
            PathComparer.GetHashCode(ProjectRoot));
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

internal sealed record FastForwardPullResult(
    RemoteOpResult Result,
    CheckedOutBranchTip Tip,
    PullTransitionState TransitionState = PullTransitionState.Unchanged);

internal sealed record ProjectCheckpoint(
    string RefName,
    string Commit,
    CheckedOutBranchTip BaseTip);

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

public sealed record InitOptions(
    RepositoryInfo TargetRepository,
    bool UseLfsWhenAvailable = true);

internal static partial class GitDiagnosticSanitizer
{
    internal static string RedactCredentials(string value)
    {
        return string.IsNullOrEmpty(value)
            ? value
            : CredentialUrlRegex().Replace(value, "${scheme}***@");
    }

    [GeneratedRegex(
        @"(?<scheme>[a-z][a-z0-9+.-]*://)[^/\s@]+@",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialUrlRegex();
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
           || Stderr.Contains("HEAD.lock", StringComparison.OrdinalIgnoreCase);

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
