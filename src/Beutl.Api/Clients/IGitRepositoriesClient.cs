using Refit;

namespace Beutl.Api.Clients;

internal interface IGitRepositoriesClient
{
    [Post("/api/v3/repos")]
    Task<HostedGitRepositoryResponse> CreateRepository(
        [Header("Authorization")] string authorization,
        [Body] CreateHostedGitRepositoryRequest request,
        CancellationToken cancellationToken);

    [Post("/api/v3/repos/{id}/token")]
    Task<HostedGitTokenResponse> IssueToken(
        Guid id,
        [Header("Authorization")] string authorization,
        [Body] HostedGitTokenRequest request,
        CancellationToken cancellationToken);
}

internal sealed record CreateHostedGitRepositoryRequest(string Name);
internal sealed record HostedGitTokenRequest(string Scope);
public sealed record HostedGitRepositoryResponse(Guid Id, string Name, string Url);
public sealed record HostedGitTokenResponse(string Token, DateTimeOffset ExpiresAt);
