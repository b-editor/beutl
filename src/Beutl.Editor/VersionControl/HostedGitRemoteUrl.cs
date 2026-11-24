using System.Diagnostics.CodeAnalysis;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Services;

namespace Beutl.Editor.VersionControl;

internal sealed class HostedGitRemoteUrl
{
    private const string GitPath = "/api/v3/git/";
    private readonly string _dashboard;

    private HostedGitRemoteUrl(Guid repositoryId, string remoteUrl, Uri dashboardOrigin)
    {
        RepositoryId = repositoryId.ToString("D");
        OriginUrl = remoteUrl;
        _dashboard = $"{dashboardOrigin.GetLeftPart(UriPartial.Authority)}/dashboard/repositories/{RepositoryId}";
    }

    public string RepositoryId { get; }

    public string OriginUrl { get; }

    public static bool TryParse(string? remoteUrl, [NotNullWhen(true)] out HostedGitRemoteUrl? hosted)
        => TryParse(remoteUrl, BeutlApiApplication.BaseUrl, out hosted);

    public static bool TryParse(string? remoteUrl, string baseUrl, [NotNullWhen(true)] out HostedGitRemoteUrl? hosted)
    {
        hosted = null;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? origin)
            || !TryMatchOrigin(remoteUrl, origin, out Guid id))
        {
            return false;
        }

        hosted = new HostedGitRemoteUrl(id, remoteUrl!, origin);
        return true;
    }

    internal static bool TryParseAdvertised(string? remoteUrl, GitRepositorySummary? repository,
        [NotNullWhen(true)] out HostedGitRemoteUrl? hosted)
    {
        hosted = null;
        if (repository is null
            || !Uri.TryCreate(repository.Url, UriKind.Absolute, out Uri? advertised)
            || !TryGetRepositoryId(advertised, out Guid advertisedId)
            || !string.Equals(advertisedId.ToString("D"), repository.Id, StringComparison.Ordinal)
            || !TryMatchOrigin(remoteUrl, advertised, out Guid id)
            || id != advertisedId)
        {
            return false;
        }

        hosted = new HostedGitRemoteUrl(id, remoteUrl!, new Uri(BeutlApiApplication.BaseUrl));
        return true;
    }

    internal static async Task<HostedGitRemoteUrl?> ResolveAsync(
        string? remoteUrl, BeutlApiApplication? api, CancellationToken cancellationToken,
        HostedGitRemoteUrl? known = null)
    {
        if (known is not null && string.Equals(known.OriginUrl, remoteUrl, StringComparison.Ordinal))
            return known;
        if (TryParse(remoteUrl, out HostedGitRemoteUrl? hosted))
            return hosted;
        if (!Uri.TryCreate(remoteUrl, UriKind.Absolute, out Uri? remote) || !TryGetRepositoryId(remote, out Guid id))
            return null;

        GitRepositorySummary? advertised = await GetAdvertisedRepositoryAsync(api, id, cancellationToken).ConfigureAwait(false);
        return TryParseAdvertised(remoteUrl, advertised, out hosted) ? hosted : null;
    }

    internal static async Task<GitRepositorySummary?> GetAdvertisedRepositoryAsync(
        BeutlApiApplication? api, Guid id, CancellationToken cancellationToken)
    {
        if (api?.AuthenticatedUser.Value is not { } owner)
            return null;

        try
        {
            GitRepositorySummary repository = await api.GetResource<HostedGitRepositoryService>()
                .GetAsync(id, cancellationToken, owner).ConfigureAwait(false);
            return ReferenceEquals(api.AuthenticatedUser.Value, owner) ? repository : null;
        }
        catch (HostedGitApiException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static bool TryMatchOrigin(string? remoteUrl, Uri expectedOrigin, out Guid id)
    {
        id = default;
        return Uri.TryCreate(remoteUrl, UriKind.Absolute, out Uri? remote)
            && string.Equals(expectedOrigin.Scheme, remote.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(expectedOrigin.IdnHost, remote.IdnHost, StringComparison.OrdinalIgnoreCase)
            && expectedOrigin.Port == remote.Port
            && TryGetRepositoryId(remote, out id);
    }

    // The authenticated API may advertise a separate public origin for Git traffic.
    internal static bool TryGetRepositoryId(Uri uri, out Guid id)
    {
        id = default;
        if (uri.Scheme is not ("http" or "https") || !uri.IsWellFormedOriginalString()
            || uri.GetLeftPart(UriPartial.Authority).Contains('@')
            || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            return false;

        string path = uri.AbsolutePath.TrimEnd('/');
        return path.Length == GitPath.Length + 36 + 4
            && path.StartsWith(GitPath, StringComparison.OrdinalIgnoreCase)
            && path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParseExact(path.AsSpan(GitPath.Length, 36), "D", out id)
            && id != Guid.Empty;
    }

    public Uri GetRepositoryUri(string? refName = null) => WithRef(_dashboard, refName);

    public Uri GetCommitsUri(string? refName = null) => WithRef($"{_dashboard}/commits", refName);

    private static Uri WithRef(string url, string? refName)
        => new(string.IsNullOrEmpty(refName) ? url : $"{url}?ref={Uri.EscapeDataString(refName)}");
}
