namespace Beutl.Editor.VersionControl;

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

    public string RepoRoot { get; }

    public string ProjectRoot { get; }

    public bool IsNestedInForeignRepo { get; }

    public string Pathspec { get; }
}

public enum SnapshotKind
{
    Manual,
    Save,
    Close,
    Safety,
    Restore,
    Init,
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

public abstract record CommitResult
{
    private CommitResult()
    {
    }

    public sealed record NoChanges : CommitResult;

    public sealed record Committed(string Sha) : CommitResult;

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

    public sealed record Failed(string Stderr) : RemoteOpResult;
}

public sealed record BranchInfo(string Name, bool IsCurrent, string? UpstreamName);

public sealed record RemoteInfo(string Name, string Url);

public sealed record GitIdentity(string Name, string Email);

public enum VersionControlPolicyNoticeKind
{
    LfsRemoteQuota,
    LargeMediaWithoutLfs,
}

public sealed record VersionControlPolicyNotice(
    VersionControlPolicyNoticeKind Kind,
    string? Path = null,
    long? SizeBytes = null);

public sealed record RepositoryLockInfo(
    string LockPath,
    DateTimeOffset LastWriteTimeUtc);

public sealed record InitOptions(
    RepositoryInfo TargetRepository,
    bool UseLfsWhenAvailable = true);

public sealed class GitOperationException : Exception
{
    public GitOperationException(int exitCode, string stderr)
        : base(CreateMessage(exitCode, stderr))
    {
        ExitCode = exitCode;
        Stderr = stderr;
    }

    public int ExitCode { get; }

    public string Stderr { get; }

    public bool IsRepositoryLockFailure
        => Stderr.Contains("another git process seems to be running", StringComparison.OrdinalIgnoreCase)
           || Stderr.Contains("index.lock", StringComparison.OrdinalIgnoreCase);

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
