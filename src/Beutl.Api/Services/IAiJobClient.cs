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

    // The job a request sent under idempotencyKey made.
    Task<AiJob> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    // Runs a finished job's request again as a new job, which idempotencyKey names.
    Task<AiJobId> RetryAsync(AiJobId jobId, string idempotencyKey, CancellationToken cancellationToken);
}
