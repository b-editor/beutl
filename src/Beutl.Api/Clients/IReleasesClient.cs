using Refit;

namespace Beutl.Api.Clients;

public interface IReleasesClient
{
    [Get("/api/v3/packages/{name}/releases")]
    Task<ReleaseResponse[]> GetReleases(string name, int start = 0, int count = 30);

    [Get("/api/v3/packages/{name}/releases/{version}")]
    Task<ReleaseResponse> GetRelease(string name, string version);
}
