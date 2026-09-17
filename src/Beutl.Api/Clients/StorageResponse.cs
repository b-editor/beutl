using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

internal sealed record StorageResponse
{
    [JsonPropertyName("entries")] public required StorageEntryResponse[] Entries { get; init; }
    [JsonPropertyName("path")] public required StorageFolderResponse[] Path { get; init; }
    [JsonPropertyName("parentId")] public required string? ParentId { get; init; }
    [JsonPropertyName("nextCursor")] public required string? NextCursor { get; init; }
}

internal sealed record StorageEntryResponse
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("parentId")] public string? ParentId { get; init; }
    [JsonPropertyName("size")] public long Size { get; init; }
    [JsonPropertyName("mimeType")] public string MimeType { get; init; } = "";
    [JsonPropertyName("visibility")] public string Visibility { get; init; } = "";
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")] public DateTimeOffset UpdatedAt { get; init; }
    [JsonPropertyName("actions")] public string[] Actions { get; init; } = [];
    [JsonPropertyName("contentUrl")] public string? ContentUrl { get; init; }
}

internal sealed record StorageFolderResponse
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("parentId")] public required string? ParentId { get; init; }
}

internal sealed record StorageFolderDetailsResponse
{
    [JsonPropertyName("folder")] public required StorageFolderResponse Folder { get; init; }
    [JsonPropertyName("ancestors")] public required StorageFolderResponse[] Ancestors { get; init; }
    [JsonPropertyName("fileCount")] public int FileCount { get; init; }
    [JsonPropertyName("folderCount")] public int FolderCount { get; init; }
}

internal sealed record StorageMutationResponse
{
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("affected")] public int Affected { get; init; }
    [JsonPropertyName("deletedFiles")] public int DeletedFiles { get; init; }
    [JsonPropertyName("deletedFolders")] public int DeletedFolders { get; init; }
}

internal sealed record StorageUploadResponse
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("partSize")] public required int PartSize { get; init; }
    [JsonPropertyName("partCount")] public required int PartCount { get; init; }
}

internal sealed record StorageUploadPartResponse
{
    [JsonPropertyName("partNumber")] public required int PartNumber { get; init; }
    [JsonPropertyName("etag")] public required string Etag { get; init; }
}

internal sealed record StorageUsageResponse
{
    [JsonPropertyName("plan")] public required string? Plan { get; init; }
    [JsonPropertyName("quotaBytes")] public required long QuotaBytes { get; init; }
    [JsonPropertyName("usedBytes")] public required long UsedBytes { get; init; }
    [JsonPropertyName("fileCount")] public required int FileCount { get; init; }
    [JsonPropertyName("fileCountLimit")] public required int FileCountLimit { get; init; }
}
