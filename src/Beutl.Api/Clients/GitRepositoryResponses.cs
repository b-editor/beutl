using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

public sealed record GitRepositorySummary(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt);

public sealed record GitRepositoryListResponse(
    [property: JsonPropertyName("repositories")] GitRepositorySummary[] Repositories);

public sealed record CreateGitRepositoryRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("creationId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CreationId = null,
    [property: JsonPropertyName("ownerId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OwnerId = null);

public sealed record RenameGitRepositoryRequest([property: JsonPropertyName("name")] string Name);

public class GitAccessTokenSummary
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("scope")]
    public required string Scope { get; init; }

    [JsonPropertyName("hint")]
    public required string Hint { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("lastUsedAt")]
    public DateTimeOffset? LastUsedAt { get; init; }
}

public sealed record GitAccessTokenListResponse(
    [property: JsonPropertyName("tokens")] GitAccessTokenSummary[] Tokens);

public sealed record CreateGitAccessTokenRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("scope")] string Scope);

public sealed class CreatedGitAccessToken : GitAccessTokenSummary
{
    [JsonPropertyName("token")]
    public required string Token { get; init; }
}
