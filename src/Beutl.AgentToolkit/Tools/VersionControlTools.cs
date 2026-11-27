using System.ComponentModel;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.Configuration;
using Beutl.Editor.VersionControl;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed record VersionControlFileChange(string Path, string Status, string? OldPath);

public sealed record VersionControlStatusResponse(
    bool HasRepository, string? Branch, string? HeadCommit, bool IsDetachedHead,
    int Ahead, int Behind, bool HasConflicts, IReadOnlyList<VersionControlFileChange> Changes);

public sealed record VersionControlCommitSummary(
    string Sha, string ShortSha, string Subject, string AuthorName, DateTimeOffset AuthorDate, string Kind);

public sealed record VersionControlHistoryResponse(IReadOnlyList<VersionControlCommitSummary> Commits);
public sealed record CommitProjectResponse(bool Committed, string? Sha);

[McpServerToolType]
public sealed class VersionControlTools(
    AgentSessionManager sessions, IProjectSessionGateway projects, GlobalConfiguration configuration) : ToolBase
{
    private readonly GitInstallationLocator _git = new(configuration.VersionControlConfig);

    [McpServerTool(Name = "read_version_control_status", ReadOnly = true)]
    [Description("Reads the project's local Git branch, current commit, conflicts, and changed files on disk.")]
    public ValueTask<ToolResult<VersionControlStatusResponse>> ReadVersionControlStatus(
        CancellationToken cancellationToken = default)
        => ExecuteAsync(() => ReadAsync(async service =>
        {
            if (service?.Repository is null)
                return new VersionControlStatusResponse(false, null, null, false, 0, 0, false, []);

            WorkspaceStatus status = await service.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            return new VersionControlStatusResponse(
                true, status.Branch, status.HeadCommit, status.IsDetachedHead, status.Ahead, status.Behind, status.HasConflicts,
                status.Changes.Select(change => new VersionControlFileChange(
                    change.Path, change.Status.ToString(), change.OldPath)).ToArray());
        }, cancellationToken));

    [McpServerTool(Name = "read_version_control_history", ReadOnly = true)]
    [Description("Reads commits affecting the project on the current Git branch, newest first.")]
    public ValueTask<ToolResult<VersionControlHistoryResponse>> ReadVersionControlHistory(
        [Description("Number of commits to skip for pagination.")] int skip = 0,
        [Description("Maximum commits to return, from 1 to 100.")] int take = 20,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(() => ReadAsync(async service =>
        {
            if (skip < 0)
                throw Invalid("The number of commits to skip must be non-negative.", nameof(skip));
            if (take is < 1 or > 100)
                throw Invalid("Request between 1 and 100 commits.", nameof(take));
            if (service?.Repository is null) return new VersionControlHistoryResponse([]);

            IReadOnlyList<CommitInfo> commits = await service.GetHistoryAsync(skip, take, cancellationToken).ConfigureAwait(false);
            return new VersionControlHistoryResponse(commits.Select(commit => new VersionControlCommitSummary(
                commit.Sha, commit.ShortSha, commit.Subject, commit.AuthorName, commit.AuthorDate, commit.Kind.ToString())).ToArray());
        }, cancellationToken));

    [McpServerTool(Name = "commit_project")]
    [Description("Saves pending project edits and creates a local Git snapshot with the supplied message. Requires Beutl version tracking and a configured Git identity. Returns committed=false when there are no changes.")]
    public ValueTask<ToolResult<CommitProjectResponse>> CommitProject(
        [Description("Message for the manual Beutl snapshot.")] string message,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(message))
                throw Invalid("Supply a commit message.", nameof(message));

            IEditingSession session = sessions.RequireSession();
            CommitResult result;
            if (session is FileEditingSession file)
            {
                result = await CommitFileProjectAsync(file, message.Trim(), cancellationToken).ConfigureAwait(false);
            }
            else if (projects is ILiveProjectVersionControlGateway live)
            {
                result = await live.CommitAsync(session, message.Trim(), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                throw new SessionUnavailableException();
            }

            return result switch
            {
                CommitResult.Committed commit => new CommitProjectResponse(true, commit.Sha),
                CommitResult.NoChanges => new CommitProjectResponse(false, null),
                _ => throw new GitIdentityRequiredException()
            };
        });

    private async ValueTask<T> ReadAsync<T>(
        Func<IProjectVersionControlService?, Task<T>> read, CancellationToken cancellationToken)
    {
        IEditingSession session = sessions.RequireSession();
        if (session is FileEditingSession file)
        {
            string projectPath = file.ReadOnSession(() => GetProjectPath(file));
            using GitCliVersionControlService? service = await OpenFileRepositoryAsync(projectPath, cancellationToken)
                .ConfigureAwait(false);
            return await read(service).ConfigureAwait(false);
        }

        if (projects is not ILiveProjectVersionControlGateway live)
            throw new SessionUnavailableException();

        try
        {
            return await read(live.GetVersionControl(session)).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            throw new SessionUnavailableException();
        }
    }

    private async ValueTask<CommitResult> CommitFileProjectAsync(
        FileEditingSession session, string message, CancellationToken cancellationToken)
    {
        string projectPath = session.ReadOnSession(() => GetProjectPath(session));
        using GitCliVersionControlService? service = await OpenFileRepositoryAsync(projectPath, cancellationToken)
            .ConfigureAwait(false);
        if (service?.Repository is not { } repository
            || !await service.HasVersionTrackingOptInAsync(repository, cancellationToken).ConfigureAwait(false))
        {
            throw Invalid("Enable Beutl version tracking for this project before committing.", "project");
        }

        // Hold the same file-session lock as save_project through publication of the snapshot.
        // The Git service has no dependency on the session lock or a UI dispatcher.
        return session.EditOnSession(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!FilePathComparison.AreSameCanonicalPath(GetProjectPath(session), projectPath))
                throw new SessionUnavailableException();

            if (session.IsDirty)
            {
                service.CheckManualCommitBeforeSaveAsync(interactive: false, cancellationToken).GetAwaiter().GetResult();
                cancellationToken.ThrowIfCancellationRequested();
                session.Save(skipConflictCheck: false);
            }
            return service.CommitNonInteractiveAsync(message, cancellationToken).GetAwaiter().GetResult();
        }, cancellationToken);
    }

    private async Task<GitCliVersionControlService?> OpenFileRepositoryAsync(
        string projectPath, CancellationToken cancellationToken)
    {
        using var discovery = CreateBackend(repository: null);
        GitAvailability availability = await discovery.GetAvailabilityAsync(cancellationToken).ConfigureAwait(false);
        if (availability.State != GitAvailabilityState.Installed) return null;

        RepositoryInfo? repository = await discovery.DiscoverRepositoryAsync(Path.GetDirectoryName(projectPath)!, cancellationToken)
            .ConfigureAwait(false);
        return repository is not null
               && await discovery.HasCheckedOutCommitAsync(repository, cancellationToken).ConfigureAwait(false)
            ? CreateBackend(repository)
            : null;
    }

    private GitCliVersionControlService CreateBackend(RepositoryInfo? repository)
        => new(_git, repository, watcher: null, static path => new GitCliRunner(path));

    private static string GetProjectPath(FileEditingSession session)
        => session.Project.Uri?.LocalPath ?? throw new SessionUnavailableException();

    private static ReconcileException Invalid(string message, string target)
        => new(new ToolError(ErrorCode.ValidationRejected, message, target));
}
