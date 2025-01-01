using Refit;

namespace Beutl.Api.Clients;

public interface IUsersClient
{
    [Get("/api/v3/users/{name}")]
    Task<ProfileResponse> GetUser(string name);

    [Get("/api/v3/user")]
    Task<ProfileResponse> GetSelf();

    [Get("/api/v3/users/{name}/packages")]
    Task<SimplePackageResponse[]> GetUserPackages(string name, int start = 0, int count = 30);
}
