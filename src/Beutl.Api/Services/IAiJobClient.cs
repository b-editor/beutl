namespace Beutl.Api.Services;

/// <summary>
/// Stateless asynchronous client for AI job history operations.
/// </summary>
public interface IAiJobClient : IBeutlApiResource
{
    Task<AiJobPage> GetPageAsync(
        AiJobPageRequest request,
        CancellationToken cancellationToken);

    Task DeleteAsync(AiJobId jobId, CancellationToken cancellationToken);
}
