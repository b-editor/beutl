using Refit;

namespace Beutl.Api.Clients;

public interface IAppClient
{
    [Get("/api/v1/app/checkForUpdates/{version}")]
    Task<CheckForUpdatesResponse> CheckForUpdates(string version);
}
