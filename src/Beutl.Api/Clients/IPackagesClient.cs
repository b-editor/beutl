using Refit;

namespace Beutl.Api.Clients;

public interface IPackagesClient
{
    [Get("/api/v3/packages/{name}")]
    Task<PackageResponse> GetPackage(string name);
}
