using Refit;

namespace Beutl.Api.Clients;

public interface IDiscoverClient
{
    [Get("/api/v3/discover/search")]
    Task<SimplePackageResponse[]> Search(
        string query,
        CancellationToken cancellationToken,
        int start = 0,
        int count = 30,
        string type = "all");

    [Get("/api/v3/discover/featured")]
    Task<SimplePackageResponse[]> GetFeatured(
        CancellationToken cancellationToken,
        int start = 0,
        int count = 30,
        string type = "all");
}
