using Refit;

namespace Beutl.Api.Clients;

public interface IFilesClient
{
    [Get("/api/v3/files/{id}")]
    Task<FileResponse> GetFile(string id);
}
