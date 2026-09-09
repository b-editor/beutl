using Beutl.Api.Services;

namespace Beutl.Services.AI;

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
        CancellationToken cancellationToken)
    {
        int transientFailures = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AiVideoJob job;
            try
            {
                job = await videos.GetAsync(jobId, cancellationToken);
                transientFailures = 0;
            }
            catch (Exception ex) when (IsTransientFailure(ex, cancellationToken))
            {
                transientFailures++;
                processing();
                double multiplier = Math.Pow(2, Math.Min(transientFailures - 1, 10));
                double milliseconds = Math.Min(
                    pollInterval.TotalMilliseconds * multiplier,
                    maximumRetryDelay.TotalMilliseconds);
                await delay(TimeSpan.FromMilliseconds(Math.Max(0, milliseconds)), cancellationToken);
                continue;
            }

            AiJobStatusSemantics status = jobKinds.GetStatus(AiJobKinds.Video, job.Status);
            if (status.Outcome == AiJobOutcomes.Succeeded || status.IsTerminal || !status.ShouldPoll)
                return (job, status);

            processing();
            await delay(pollInterval, cancellationToken);
        }
    }

    private static bool IsTransientFailure(Exception exception, CancellationToken cancellationToken)
        => !cancellationToken.IsCancellationRequested
            && (exception is OperationCanceledException
                or TimeoutException
                or HttpRequestException
                or AiException { IsTransient: true }
                || exception.InnerException is { } inner && IsTransientFailure(inner, cancellationToken));
}
