using System.Runtime.CompilerServices;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Configuration;

namespace Beutl.Editor.VersionControl;

internal sealed class LocalRepositoryLocator(GitInstallationLocator installation, BeutlApiApplication? api = null)
{
    public LocalRepositoryLocator(VersionControlConfig config) : this(new GitInstallationLocator(config))
    {
    }

    public Task<string?> FindRecentProjectAsync(
        ViewConfig config, Guid repositoryId, CancellationToken cancellationToken, string? revision = null)
        => FindProjectAsync(config.RecentProjects.ToArray(), repositoryId, cancellationToken, revision);

    public Task<string?> FindProjectAsync(
        string[] paths, Guid repositoryId, CancellationToken cancellationToken, string? revision = null)
        => Task.Run(() => FindProjectCoreAsync(paths, repositoryId, cancellationToken, revision), cancellationToken);

    public Task<Dictionary<string, Guid>> FindProjectRepositoriesAsync(
        string[] paths, IReadOnlyList<GitRepositorySummary> repositories, CancellationToken cancellationToken)
        => Task.Run(async () =>
        {
            var advertised = new Dictionary<Guid, GitRepositorySummary>();
            foreach (GitRepositorySummary repository in repositories)
            {
                if (Guid.TryParseExact(repository.Id, "D", out Guid id))
                    advertised.TryAdd(id, repository);
            }

            var matches = new Dictionary<string, Guid>(StringComparer.Ordinal);
            if (advertised.Count == 0)
                return matches;

            await foreach ((string path, string origin) in ReadOriginsAsync(paths, cancellationToken).ConfigureAwait(false))
            {
                if (Uri.TryCreate(origin, UriKind.Absolute, out Uri? remote)
                    && HostedGitRemoteUrl.TryGetRepositoryId(remote, out Guid id)
                    && advertised.TryGetValue(id, out GitRepositorySummary? repository)
                    && HostedGitRemoteUrl.TryParseAdvertised(origin, repository, out _))
                {
                    matches.TryAdd(path, id);
                }
            }
            return matches;
        }, cancellationToken);

    private async Task<string?> FindProjectCoreAsync(
        string[] paths, Guid repositoryId, CancellationToken cancellationToken, string? revision)
    {
        string id = repositoryId.ToString("D");
        Task<(GitRepositorySummary? Repository, bool RetryOnRefresh)>? advertisement = null;
        await foreach ((string path, string origin) in ReadOriginsAsync(paths, cancellationToken, revision).ConfigureAwait(false))
        {
            if (HostedGitRemoteUrl.TryParse(origin, out HostedGitRemoteUrl? remote) && remote.RepositoryId == id)
                return path;
            if (api is not null
                && Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
                && HostedGitRemoteUrl.TryGetRepositoryId(uri, out Guid candidate)
                && candidate == repositoryId)
            {
                advertisement ??= HostedGitRemoteUrl.GetAdvertisedRepositoryAsync(api, repositoryId, cancellationToken);
                var (repository, _) = await advertisement.ConfigureAwait(false);
                if (HostedGitRemoteUrl.TryParseAdvertised(origin, repository, out _))
                    return path;
            }
        }
        return null;
    }

    private async IAsyncEnumerable<(string Path, string Origin)> ReadOriginsAsync(
        string[] paths, [EnumeratorCancellation] CancellationToken cancellationToken, string? revision = null)
    {
        string? gitPath = (await installation.LocateAsync(cancellationToken).ConfigureAwait(false)).GitPath;
        if (gitPath is null)
            yield break;

        var runner = new GitCliRunner(gitPath);
        var origins = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path) || Path.GetDirectoryName(path) is not { } directory)
                continue;

            string? url;
            try
            {
                var context = new RepositoryInfo(directory, directory);
                string root = (await runner.RunAsync(context, ["rev-parse", "--show-toplevel"],
                    GitCommandOptions.Local, cancellationToken).ConfigureAwait(false)).Stdout.TrimEnd('\r', '\n');
                if (!origins.TryGetValue(root, out url))
                {
                    var repository = new RepositoryInfo(root, directory);
                    url = await GitCliVersionControlService.GetOriginUrlAsync(repository, runner, cancellationToken)
                        .ConfigureAwait(false);
                    if (revision is not null
                        && Uri.TryCreate(url, UriKind.Absolute, out Uri? remote)
                        && HostedGitRemoteUrl.TryGetRepositoryId(remote, out _)
                        && !await MatchesCheckoutAsync(repository, runner, revision, cancellationToken).ConfigureAwait(false))
                    {
                        url = null;
                    }
                    origins.Add(root, url);
                }
            }
            catch (Exception ex) when (ex is GitOperationException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A stale or inaccessible recent entry does not block the other candidates.
                continue;
            }
            if (url is not null)
                yield return (path, url);
        }
    }

    private static async Task<bool> MatchesCheckoutAsync(
        RepositoryInfo repository, IGitCliRunner runner, string revision, CancellationToken cancellationToken)
    {
        const string heads = "refs/heads/";
        try
        {
            string? branchRef = revision.StartsWith(heads, StringComparison.Ordinal) ? revision : null;
            string target = revision == "HEAD" ? "refs/remotes/origin/HEAD" : revision;
            bool fullObjectId = revision.Length is 40 or 64 && GitRevisionValidator.IsHexadecimalRevision(revision);
            if (!fullObjectId && revision != "HEAD" && !revision.StartsWith("refs/", StringComparison.Ordinal))
            {
                string candidate = heads + revision;
                if (await GitCliVersionControlService.RefExistsAsync(repository, runner, $"refs/remotes/origin/{revision}",
                        cancellationToken).ConfigureAwait(false))
                {
                    branchRef = candidate;
                }
                else if (!GitRevisionValidator.IsAbbreviatedObjectId(revision))
                {
                    // Clone resolves a remote branch or tag. Allow a local-only branch as a fallback.
                    string tag = $"refs/tags/{revision}";
                    if (await GitCliVersionControlService.RefExistsAsync(repository, runner, tag, cancellationToken)
                            .ConfigureAwait(false))
                    {
                        target = tag;
                    }
                    else if (await GitCliVersionControlService.RefExistsAsync(repository, runner, candidate, cancellationToken)
                                 .ConfigureAwait(false))
                    {
                        branchRef = candidate;
                    }
                }
            }

            if (branchRef is not null)
            {
                string currentBranch = (await runner.RunAsync(repository, ["symbolic-ref", "--quiet", "HEAD"],
                    GitCommandOptions.Local, cancellationToken).ConfigureAwait(false)).Stdout.TrimEnd('\r', '\n');
                return currentBranch == branchRef;
            }

            string? requestedCommit = await GitCliVersionControlService.TryResolveHostedCommitAsync(repository, runner, target,
                cancellationToken).ConfigureAwait(false);
            return requestedCommit is not null
                && requestedCommit == await GitCliVersionControlService.TryResolveCommitAsync(repository, runner, "HEAD",
                    cancellationToken).ConfigureAwait(false);
        }
        catch (GitOperationException)
        {
            return false;
        }
    }
}
