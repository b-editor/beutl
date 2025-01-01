using Refit;

namespace Beutl.Api.Clients;

public interface IDiscoverClient
{
    [Get("/api/v3/discover/search")]
    Task<SimplePackageResponse[]> Search(string query, int start = 0, int count = 30);

    [Get("/api/v3/discover/featured")]
    Task<SimplePackageResponse[]> GetFeatured(int start = 0, int count = 30);
}
