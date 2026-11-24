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

    public Task<string?> FindRecentProjectAsync(ViewConfig config, Guid repositoryId, CancellationToken cancellationToken)
        => FindProjectAsync(config.RecentProjects.ToArray(), repositoryId, cancellationToken);

    public Task<string?> FindProjectAsync(string[] paths, Guid repositoryId, CancellationToken cancellationToken)
        => Task.Run(() => FindProjectCoreAsync(paths, repositoryId, cancellationToken), cancellationToken);

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

    private async Task<string?> FindProjectCoreAsync(string[] paths, Guid repositoryId, CancellationToken cancellationToken)
    {
        string id = repositoryId.ToString("D");
        Task<GitRepositorySummary?>? advertisement = null;
        await foreach ((string path, string origin) in ReadOriginsAsync(paths, cancellationToken).ConfigureAwait(false))
        {
            if (HostedGitRemoteUrl.TryParse(origin, out HostedGitRemoteUrl? remote) && remote.RepositoryId == id)
                return path;
            if (api is not null
                && Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
                && HostedGitRemoteUrl.TryGetRepositoryId(uri, out Guid candidate)
                && candidate == repositoryId)
            {
                advertisement ??= HostedGitRemoteUrl.GetAdvertisedRepositoryAsync(api, repositoryId, cancellationToken);
                if (HostedGitRemoteUrl.TryParseAdvertised(origin, await advertisement.ConfigureAwait(false), out _))
                    return path;
            }
        }
        return null;
    }

    private async IAsyncEnumerable<(string Path, string Origin)> ReadOriginsAsync(
        string[] paths, [EnumeratorCancellation] CancellationToken cancellationToken)
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
}
