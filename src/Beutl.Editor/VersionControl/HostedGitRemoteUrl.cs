using System.Diagnostics.CodeAnalysis;
using System.Net;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Services;

namespace Beutl.Editor.VersionControl;

internal sealed class HostedGitRemoteUrl
{
    private const string GitPath = "/api/v3/git/";
    private readonly string _dashboard;

    private HostedGitRemoteUrl(Guid repositoryId, Uri dashboardOrigin)
    {
        RepositoryId = repositoryId.ToString("D");
        _dashboard = $"{dashboardOrigin.GetLeftPart(UriPartial.Authority)}/dashboard/repositories/{RepositoryId}";
    }

    public string RepositoryId { get; }

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

        hosted = new HostedGitRemoteUrl(id, origin);
        return true;
    }

    internal static bool TryParseAdvertised(string? remoteUrl, GitRepositorySummary? repository,
        [NotNullWhen(true)] out HostedGitRemoteUrl? hosted)
    {
        hosted = null;
        if (repository is null
            || !TryGetAdvertisedUrl(repository, out Uri? advertised, out Guid advertisedId)
            || !TryMatchOrigin(remoteUrl, advertised, out Guid id)
            || id != advertisedId)
        {
            return false;
        }

        hosted = new HostedGitRemoteUrl(id, new Uri(BeutlApiApplication.BaseUrl));
        return true;
    }

    internal static async Task<(HostedGitRemoteUrl? Remote, bool RetryOnRefresh)> ResolveAsync(
        string? remoteUrl, BeutlApiApplication? api, CancellationToken cancellationToken)
    {
        if (TryParse(remoteUrl, out HostedGitRemoteUrl? hosted))
            return (hosted, false);
        if (!TryGetRepositoryId(remoteUrl, out Guid id))
            return default;

        var (advertised, retry) = await GetAdvertisedRepositoryAsync(api, id, cancellationToken).ConfigureAwait(false);
        return (TryParseAdvertised(remoteUrl, advertised, out hosted) ? hosted : null, retry);
    }

    internal static async Task<(GitRepositorySummary? Repository, bool RetryOnRefresh)> GetAdvertisedRepositoryAsync(
        BeutlApiApplication? api, Guid id, CancellationToken cancellationToken)
    {
        if (api?.AuthenticatedUser.Value is not { } owner)
            return default;

        try
        {
            GitRepositorySummary repository = await api.GetResource<HostedGitRepositoryService>()
                .GetAsync(id, cancellationToken, owner).ConfigureAwait(false);
            return (ReferenceEquals(api.AuthenticatedUser.Value, owner) ? repository : null, false);
        }
        catch (HostedGitApiException ex)
        {
            bool retry = ex.Error == HostedGitError.Unavailable
                || ex.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                || (int?)ex.StatusCode >= 500;
            return (null, retry);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, true);
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

    internal static bool TryGetRepositoryId(string? url, out Guid id)
    {
        id = default;
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && TryGetRepositoryId(uri, out id);
    }

    // The Git URL that a repository summary advertises, when that URL names the same repository.
    internal static bool TryGetAdvertisedUrl(GitRepositorySummary repository, [NotNullWhen(true)] out Uri? url, out Guid id)
    {
        id = default;
        return Uri.TryCreate(repository.Url, UriKind.Absolute, out url)
            && TryGetRepositoryId(url, out id)
            && string.Equals(id.ToString("D"), repository.Id, StringComparison.Ordinal);
    }

    public Uri GetRepositoryUri(string? refName = null) => WithRef(_dashboard, refName);

    private static Uri WithRef(string url, string? refName)
        => new(string.IsNullOrEmpty(refName) ? url : $"{url}?ref={Uri.EscapeDataString(refName)}");
}
