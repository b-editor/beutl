using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

// Forwards every call to the inner runner; fakes override RunAsync to intercept commands.
internal class DelegatingGitCliRunner(IGitCliRunner inner) : IGitCliRunner
{
    public string? TemporaryIndexPath { get; private set; }

    public virtual Task<GitCommandResult> RunAsync(
        RepositoryInfo repository,
        IReadOnlyList<string> arguments,
        GitCommandOptions options,
        CancellationToken cancellationToken,
        IProgress<string>? stderrProgress = null)
        => inner.RunAsync(repository, arguments, options, cancellationToken, stderrProgress);

    public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
        => inner.GetRecoverableRepositoryLock(repository);

    protected void RecordTemporaryPaths(
        IReadOnlyList<string> arguments,
        GitCommandOptions options)
    {
        if (options.EnvironmentOverrides?.TryGetValue(
                "GIT_INDEX_FILE",
                out string? indexPath) == true)
        {
            TemporaryIndexPath = indexPath;
        }
    }
}

internal sealed record RecordedCommand(
    RepositoryInfo Repository,
    IReadOnlyList<string> Arguments,
    GitCommandOptions Options);

internal sealed class RecordingGitCliRunner(IGitCliRunner inner) : DelegatingGitCliRunner(inner)
{
    public List<RecordedCommand> Commands { get; } = [];

    public override Task<GitCommandResult> RunAsync(
        RepositoryInfo repository,
        IReadOnlyList<string> arguments,
        GitCommandOptions options,
        CancellationToken cancellationToken,
        IProgress<string>? stderrProgress = null)
    {
        lock (Commands)
        {
            Commands.Add(new RecordedCommand(repository, [.. arguments], options));
        }

        return base.RunAsync(repository, arguments, options, cancellationToken, stderrProgress);
    }
}
