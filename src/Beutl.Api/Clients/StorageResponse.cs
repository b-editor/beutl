using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

internal sealed record StorageResponse
{
    [JsonPropertyName("files")] public required StorageFileResponse[] Files { get; init; }
    [JsonPropertyName("folders")] public required StorageFolderResponse[] Folders { get; init; }
    [JsonPropertyName("folderId")] public required string? FolderId { get; init; }
    [JsonPropertyName("total")] public required int Total { get; init; }
    [JsonPropertyName("page")] public required int Page { get; init; }
    [JsonPropertyName("pageCount")] public required int PageCount { get; init; }
    [JsonPropertyName("usage")] public required StorageUsageResponse Usage { get; init; }
}

internal sealed record StorageFileResponse
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("size")] public required long Size { get; init; }
    [JsonPropertyName("mimeType")] public required string MimeType { get; init; }
    [JsonPropertyName("visibility")] public required string Visibility { get; init; }
    [JsonPropertyName("createdAt")] public required DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("folderId")] public required string? FolderId { get; init; }
}

internal sealed record StorageFolderResponse
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("parentId")] public required string? ParentId { get; init; }
}

internal sealed record StorageUsageResponse
{
    // The API uses null for the free allowance; paid tiers are "100gb", "200gb", or "1tb".
    [JsonPropertyName("plan")] public required string? Plan { get; init; }
    [JsonPropertyName("quotaBytes")] public required long QuotaBytes { get; init; }
    [JsonPropertyName("usedBytes")] public required long UsedBytes { get; init; }
    [JsonPropertyName("fileCount")] public required int FileCount { get; init; }
    [JsonPropertyName("fileCountLimit")] public required int FileCountLimit { get; init; }
}
