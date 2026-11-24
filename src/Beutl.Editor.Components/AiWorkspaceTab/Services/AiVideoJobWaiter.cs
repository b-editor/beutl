using Beutl.Api.Services;

namespace Beutl.Editor.Components.AiWorkspaceTab.Services;

internal static class AiVideoJobWaiter
{
    // Unknown statuses return to the caller without settling the paid request.
    public static async Task<(AiVideoJob Job, AiJobStatusSemantics Status)> WaitAsync(
        IAiVideoService videos,
        IAiJobKindRegistry jobKinds,
        AiJobId jobId,
        Action processing,
        TimeSpan pollInterval,
        TimeSpan maximumRetryDelay,
        Func<TimeSpan, CancellationToken, Task> delay,
        AiAccountJobScope account,
        CancellationToken cancellationToken)
    {
        int transientFailures = 0;
        while (true)
        {
            account.ThrowIfInvalid(cancellationToken);
            AiVideoJob job;
            try
            {
                job = await account.SendAsync(token => videos.GetAsync(jobId, token), cancellationToken);
                transientFailures = 0;
            }
            catch (Exception ex) when (!account.AccountChanged && IsTransientFailure(ex, cancellationToken))
            {
                transientFailures++;
                processing();
                double multiplier = Math.Pow(2, Math.Min(transientFailures - 1, 10));
                double milliseconds = Math.Min(
                    pollInterval.TotalMilliseconds * multiplier,
                    maximumRetryDelay.TotalMilliseconds);
                account.ThrowIfInvalid(cancellationToken);
                await delay(TimeSpan.FromMilliseconds(Math.Max(0, milliseconds)), cancellationToken);
                continue;
            }

            account.ThrowIfInvalid(cancellationToken);
            AiJobStatusSemantics status = jobKinds.GetStatus(AiJobKinds.Video, job.Status);
            if (status.Outcome == AiJobOutcomes.Succeeded || status.IsTerminal || !status.ShouldPoll)
                return (job, status);

            processing();
            account.ThrowIfInvalid(cancellationToken);
            await delay(pollInterval, cancellationToken);
        }
    }

    private static bool IsTransientFailure(Exception exception, CancellationToken cancellationToken)
        => !cancellationToken.IsCancellationRequested
            && exception is not AuthenticationRequiredException
            && (exception is OperationCanceledException
                or TimeoutException
                or HttpRequestException
                or AiException { IsTransient: true }
                || exception.InnerException is { } inner && IsTransientFailure(inner, cancellationToken));
}
