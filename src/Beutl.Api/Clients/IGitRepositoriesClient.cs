using Refit;

namespace Beutl.Api.Clients;

internal interface IGitRepositoriesClient
{
    [Get("/api/v3/repos")]
    Task<GitRepositoryListResponse> List([Header("Authorization")] string authorization, CancellationToken cancellationToken);

    [Post("/api/v3/repos")]
    Task<GitRepositorySummary> Create([Header("Authorization")] string authorization, [Body] CreateGitRepositoryRequest request, CancellationToken cancellationToken);

    [Get("/api/v3/repos/{id}")]
    Task<GitRepositorySummary> Get([Header("Authorization")] string authorization, string id, CancellationToken cancellationToken);

    [Patch("/api/v3/repos/{id}")]
    Task<GitRepositorySummary> Rename([Header("Authorization")] string authorization, string id, [Body] RenameGitRepositoryRequest request, CancellationToken cancellationToken);

    [Delete("/api/v3/repos/{id}")]
    Task Delete([Header("Authorization")] string authorization, string id, CancellationToken cancellationToken);

    [Get("/api/v3/repos/{id}/tokens")]
    Task<GitAccessTokenListResponse> ListTokens([Header("Authorization")] string authorization, string id, CancellationToken cancellationToken);

    [Post("/api/v3/repos/{id}/tokens")]
    Task<CreatedGitAccessToken> CreateToken([Header("Authorization")] string authorization, string id, [Body] CreateGitAccessTokenRequest request, CancellationToken cancellationToken);

    [Delete("/api/v3/repos/{id}/tokens/{tokenId}")]
    Task RevokeToken([Header("Authorization")] string authorization, string id, string tokenId, CancellationToken cancellationToken);
}
