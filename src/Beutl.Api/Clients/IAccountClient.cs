using Refit;

namespace Beutl.Api.Clients;

public interface IAccountClient
{
    [Post("/api/v1/account/createAuthUri")]
    Task<CreateAuthUriResponse> CreateAuthUri(
        [Body] CreateAuthUriRequest request,
        CancellationToken cancellationToken);

    [Post("/api/v1/account/refresh")]
    Task<AuthResponse> Refresh(
        [Body] RefreshTokenRequest request,
        CancellationToken cancellationToken);

    [Post("/api/v1/account/code2jwt")]
    Task<AuthResponse> Exchange(
        [Body] ExchangeRequest request,
        CancellationToken cancellationToken);
}
