using Beutl.Api.Services;
using Beutl.Language;

namespace Beutl.Services.AI;

// What the server holds under a pending request's key.
internal enum AiPendingJobState
{
    Succeeded,
    Failed,
    Running,
    // No job was ever made under the key, so nothing was charged.
    NotMade,
    Deleted,
}

internal static class AiPendingJobLookup
{
    public static async Task<(AiPendingJobState State, AiJob? Job)> FindAsync(
        IAiJobClient jobs,
        IAiJobKindRegistry jobKinds,
        AiRequestRecoveryContext context,
        AiPendingAttempt attempt,
        CancellationToken cancellationToken)
    {
        using IDisposable scope = context.Enter(attempt.AccountId);
        try
        {
            AiJob job = await jobs.GetByIdempotencyKeyAsync(attempt.Key, cancellationToken);
            AiJobStatusSemantics status = jobKinds.GetStatus(job);
            AiPendingJobState state = status.Outcome == AiJobOutcomes.Succeeded
                ? AiPendingJobState.Succeeded
                // An unknown status is not an outcome, so the row stays.
                : status.IsTerminal ? AiPendingJobState.Failed : AiPendingJobState.Running;
            return (state, job);
        }
        catch (AiJobNotFoundException)
        {
            return (AiPendingJobState.NotMade, null);
        }
        catch (AiRequestWasDeletedException)
        {
            return (AiPendingJobState.Deleted, null);
        }
    }

    // What to show for a row the lookup settled without a result.
    public static string MessageFor(AiPendingJobState state, AiJob? job) => state switch
    {
        AiPendingJobState.NotMade => Strings.AiPendingRequestNotReceived,
        AiPendingJobState.Deleted => Strings.AiRequestWasDeleted,
        _ => AiErrorMessage.Localize(job?.Error) ?? Strings.AiProviderError,
    };
}
