using System.Diagnostics;
using Beutl.Api.Clients;
using Refit;

namespace Beutl.Api.Services;

internal sealed class AiJobClient(BeutlApiApplication application) : IAiJobClient
{
    public async Task<AiJobPage> GetPageAsync(
        AiJobPageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using CancellationTokenSource operationCts =
            application.CreateLifetimeLinkedTokenSource(cancellationToken);
        using Activity? activity = application.ActivitySource.StartActivity(
            "AiJobClient.GetPage",
            ActivityKind.Client);
        try
        {
            AuthenticatedApiResult<AiJobHistoryPageResponse> response =
                await application.SendAuthenticatedAsync(
                    (authorization, token) => application.Ai.GetJobs(
                        authorization,
                        request.Cursor,
                        request.Limit,
                        token),
                    operationCts.Token);
            AiJob[] jobs = (response.Value.Jobs ?? [])
                .Select(AiModelMapper.ToModel)
                .ToArray();
            return new AiJobPage(
                [.. jobs],
                string.IsNullOrWhiteSpace(response.Value.NextCursor)
                    ? null
                    : response.Value.NextCursor);
        }
        catch (ApiException ex)
        {
            throw await AiErrorConverter.ConvertAsync(ex, activity);
        }
    }

    public async Task DeleteAsync(AiJobId jobId, CancellationToken cancellationToken)
    {
        if (jobId.Value.Length == 0)
            throw new ArgumentException("A job identifier is required.", nameof(jobId));
        using CancellationTokenSource operationCts =
            application.CreateLifetimeLinkedTokenSource(cancellationToken);
        using Activity? activity = application.ActivitySource.StartActivity(
            "AiJobClient.Delete",
            ActivityKind.Client);
        activity?.SetTag("jobId", jobId.Value);
        try
        {
            AuthenticatedApiResult<DeleteAiJobResponse> response =
                await application.SendAuthenticatedAsync(
                    (authorization, token) => application.Ai.DeleteJob(
                        authorization,
                        jobId.Value,
                        token),
                    operationCts.Token);
            if (!response.Value.Deleted)
                throw new AiException("The AI job was not deleted.");
        }
        catch (ApiException ex)
        {
            throw await AiErrorConverter.ConvertAsync(ex, activity);
        }
    }
}
