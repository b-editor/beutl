using System.Diagnostics.CodeAnalysis;
using Beutl.Api;

namespace Beutl.Editor.VersionControl;

internal sealed class HostedGitRemoteUrl
{
    private const string GitPath = "/api/v3/git/";
    private readonly string _dashboard;

    private HostedGitRemoteUrl(Guid repositoryId, Uri origin)
    {
        RepositoryId = repositoryId.ToString("D");
        _dashboard = $"{origin.GetLeftPart(UriPartial.Authority)}/dashboard/repositories/{RepositoryId}";
    }

    public string RepositoryId { get; }

    public static bool TryParse(string? remoteUrl, [NotNullWhen(true)] out HostedGitRemoteUrl? hosted)
        => TryParse(remoteUrl, BeutlApiApplication.BaseUrl, out hosted);

    public static bool TryParse(string? remoteUrl, string baseUrl, [NotNullWhen(true)] out HostedGitRemoteUrl? hosted)
    {
        hosted = null;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? origin)
            || !Uri.TryCreate(remoteUrl, UriKind.Absolute, out Uri? remote)
            || !string.Equals(origin.Scheme, remote.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(origin.IdnHost, remote.IdnHost, StringComparison.OrdinalIgnoreCase)
            || origin.Port != remote.Port
            || !TryGetRepositoryId(remote, out Guid id))
        {
            return false;
        }

        hosted = new HostedGitRemoteUrl(id, origin);
        return true;
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
