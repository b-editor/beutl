using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Language;

namespace Beutl.Editor.VersionControl;

internal static class HostedGitRepositoryConnection
{
    public static Task ConfigureAsync(
        HostedGitRepositoryService service, AuthenticatedUser owner, GitRepositorySummary repository,
        Func<GitRemoteUrl, CancellationToken, Task> configure, CancellationToken cancellationToken)
    {
        if (!HostedGitRemoteUrl.TryGetAdvertisedUrl(repository, out Uri? url, out Guid id))
        {
            throw new InvalidOperationException(Strings.HostedGit_InvalidRepositoryUrl);
        }

        return service.UseDesktopTokenAsync(id, owner, async (token, ct) =>
        {
            await configure(GitRemoteUrl.WithHostedCredentials(url, token), ct);
            return true;
        }, cancellationToken);
    }
}
