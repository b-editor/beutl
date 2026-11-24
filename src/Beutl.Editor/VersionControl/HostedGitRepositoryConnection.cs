using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Language;

namespace Beutl.Editor.VersionControl;

internal static class HostedGitRepositoryConnection
{
    public static Task<T> ConfigureAsync<T>(
        HostedGitRepositoryService service, AuthenticatedUser owner, GitRepositorySummary repository,
        Func<GitRemoteUrl, CancellationToken, Task<T>> configure, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(repository.Url, UriKind.Absolute, out Uri? url)
            || !HostedGitRemoteUrl.TryGetRepositoryId(url, out Guid id)
            || !string.Equals(id.ToString("D"), repository.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(Strings.HostedGit_InvalidRepositoryUrl);
        }

        return service.UseDesktopTokenAsync(id, owner,
            (token, ct) => configure(GitRemoteUrl.WithHostedCredentials(url, token), ct), cancellationToken);
    }
}
