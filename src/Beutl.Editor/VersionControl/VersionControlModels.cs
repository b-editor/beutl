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
