using Microsoft.Extensions.Logging;

namespace Beutl.Media.Proxy;

public sealed partial class ProxyJobQueue
{
    private readonly record struct FailureRegistration(ProxyEntry? Previous, bool Changed);

    private static Exception? ReleaseAdmissionLease(IDisposable? admissionLease)
    {
        try
        {
            admissionLease?.Dispose();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private FailureRegistration RegisterFailure(ProxyJob job, string? failureReason)
    {
        if (_store == null)
            return default;

        ProxyEntry? previous;
        try
        {
            previous = _store.TryGet(job.Source, job.Preset);
        }
        catch (Exception ex)
        {
            RecordBookkeepingFailure(job, ex);
            return default;
        }

        if (previous is { State: ProxyState.Ready or ProxyState.Stale })
            return default;

        try
        {
            var now = DateTime.UtcNow;
            _store.Register(new ProxyEntry(
                job.Source,
                job.Preset,
                ProxyState.Failed,
                ProxyPathUtilities.BuildRelativePath(job.Source, job.Preset),
                0,
                default,
                default,
                now,
                now,
                failureReason));
            return new FailureRegistration(previous, Changed: true);
        }
        catch (Exception ex)
        {
            RecordBookkeepingFailure(job, ex);
            // Register may update memory before persistence throws. Treat the result as ambiguous
            // so a cancellation path restores the captured entry or deletes the attempted one.
            return new FailureRegistration(previous, Changed: true);
        }
    }

    private static void RecordBookkeepingFailure(ProxyJob job, Exception failure)
    {
        job.BookkeepingError = job.BookkeepingError is null
            ? failure
            : new AggregateException(job.BookkeepingError, failure);
        s_logger.LogError(
            failure,
            "Failed to record Failed proxy entry for {Source} ({Preset}).",
            job.Source.AbsolutePath,
            job.Preset);
    }

    private Exception? RollBackFailure(ProxyJob job, FailureRegistration registration)
    {
        if (_store is null || !registration.Changed)
        {
            return null;
        }

        try
        {
            if (registration.Previous is { } previous)
            {
                _store.Register(previous);
            }
            else
            {
                _store.Delete(job.Source, job.Preset);
            }

            ProxyEntry? restored = _store.TryGet(job.Source, job.Preset);
            if (!Equals(restored, registration.Previous))
            {
                throw new InvalidOperationException(
                    "Proxy failure bookkeeping could not be restored after cancellation.");
            }

            return null;
        }
        catch (Exception ex)
        {
            job.BookkeepingError = job.BookkeepingError is null
                ? ex
                : new AggregateException(job.BookkeepingError, ex);
            s_logger.LogError(
                ex,
                "Failed to roll back the proxy failure entry after cancellation for {Source} ({Preset}).",
                job.Source.AbsolutePath,
                job.Preset);
            return ex;
        }
    }

    private void CompleteSkippedOrCanceled(WorkItem item, string message)
    {
        if (!item.TryClaimNonCancellationTerminal(cancellationWins: true))
        {
            CompleteCanceled(item);
            return;
        }

        item.Job.Status = ProxyJobStatus.Skipped;
        item.Job.StatusMessage = message;
        OnJobChanged(item.Job, ProxyJobChangeKind.Skipped);
    }

    private void FailJob(WorkItem item, Exception failure, bool cancellationWins)
    {
        if (!item.TryClaimNonCancellationTerminal(cancellationWins))
        {
            CompleteCanceled(item);
            return;
        }

        // Record the Failed store entry before the terminal transition so an observer that sees
        // Status == Failed can already read the entry from the store.
        item.Job.Error = failure;
        RegisterFailure(item.Job, failure.Message);
        item.Job.Status = ProxyJobStatus.Failed;
        OnJobChanged(item.Job, ProxyJobChangeKind.Failed);
    }

    private sealed class AdmissionLeaseReleaseException(Exception failure)
        : Exception("The proxy-generation admission lease could not be released.", failure)
    {
        public Exception Failure { get; } = failure;
    }
}
