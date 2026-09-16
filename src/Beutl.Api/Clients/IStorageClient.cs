using Refit;

namespace Beutl.Api.Clients;

internal interface IStorageClient
{
    [Get("/api/v3/storage/entries")]
    Task<StorageResponse> GetEntries([Header("Authorization")] string authorization,
        CancellationToken cancellationToken, string? parentId = null, string? cursor = null, string? kind = null, int limit = 50);

    [Get("/api/v3/storage/usage")]
    Task<StorageUsageResponse> GetUsage([Header("Authorization")] string authorization, CancellationToken cancellationToken);

    [Get("/api/v3/storage/files/{id}")]
    Task<StorageEntryResponse> GetFile([Header("Authorization")] string authorization, string id, CancellationToken cancellationToken);

    [Get("/api/v3/storage/folders/{id}")]
    Task<StorageFolderDetailsResponse> GetFolder([Header("Authorization")] string authorization, string id, CancellationToken cancellationToken);

    [Post("/api/v3/storage/folders")]
    Task<StorageMutationResponse> CreateFolder([Header("Authorization")] string authorization, [Body] object body, CancellationToken cancellationToken);

    [Patch("/api/v3/storage/files/{id}")]
    Task UpdateFile([Header("Authorization")] string authorization, string id, [Body] object body, CancellationToken cancellationToken);

    [Patch("/api/v3/storage/folders/{id}")]
    Task UpdateFolder([Header("Authorization")] string authorization, string id, [Body] object body, CancellationToken cancellationToken);

    [Delete("/api/v3/storage/files/{id}")]
    Task DeleteFile([Header("Authorization")] string authorization, string id, CancellationToken cancellationToken);

    [Delete("/api/v3/storage/folders/{id}?recursive=true")]
    Task<StorageMutationResponse> DeleteFolderTree([Header("Authorization")] string authorization, string id, CancellationToken cancellationToken);

    [Post("/api/v3/storage/files/batch")]
    Task<StorageMutationResponse> FileBatch([Header("Authorization")] string authorization, [Body] object body, CancellationToken cancellationToken);
}
