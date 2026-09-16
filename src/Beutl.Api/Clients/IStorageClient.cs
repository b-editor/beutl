using Refit;

namespace Beutl.Api.Clients;

internal interface IStorageClient
{
    [Get("/api/v3/storage")]
    Task<StorageResponse> GetStorage(
        [Header("Authorization")] string authorization,
        CancellationToken cancellationToken,
        string? folder = null,
        int page = 1);
}
