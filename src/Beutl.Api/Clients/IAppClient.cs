using Refit;

namespace Beutl.Api.Clients;

public interface IAppClient
{
    [Get("/api/v1/app/checkForUpdates/{version}")]
    Task<CheckForUpdatesResponse> CheckForUpdates(string version);


    [Get("/api/v3/app/updates/{version}")]
    Task<AppUpdateResponse> GetUpdate(string version, string type, string os, string arch, string standalone, string prerelease);
}
