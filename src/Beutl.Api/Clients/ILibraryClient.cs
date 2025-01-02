using Refit;

namespace Beutl.Api.Clients;

public interface ILibraryClient
{
    [Post("/api/v3/account/library")]
    Task<AcquirePackageResponse> AcquirePackage([Body] AcquirePackageRequest request);

    [Get("/api/v3/account/library")]
    Task<AcquirePackageResponse[]> GetLibrary(int start = 0, int count = 30);

    [Delete("/api/v3/account/library/{name}")]
    Task DeleteLibraryPackage(string name);
}
