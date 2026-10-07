using Microsoft.Extensions.Logging;

namespace Beutl.Media.Proxy;

public sealed partial class ProxyJobQueue
{
    private bool RequeueForAdmissionRetry(WorkItem item)
    {
        if (!item.ResetForAdmissionRetry())
            return false;

        TimeSpan backoff = item.NextAdmissionBackoff(
            _minUnavailableBackoff,
            _maxUnavailableBackoff,
            out bool firstRejection);
        if (firstRejection)
        {
            if (!item.TryPublishAdmissionWaiting(() =>
                    OnJobChanged(item.Job, ProxyJobChangeKind.WaitingForAdmission)))
            {
                return false;
            }
        }

        Task retry = ResumeAdmissionAfterBackoffAsync(item, backoff);
        lock (_lock)
        {
            _admissionRetryTasks.Add(retry);
        }

        _ = RemoveAdmissionRetryWhenCompleteAsync(retry);
        return true;
    }

    private void OnAdmissionAvailabilityChanged(object? sender, EventArgs e)
    {
        List<(WorkItem Item, long Generation)> deferred = [];
        WorkItem[] items;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            items = [.. _items];
        }

        foreach (WorkItem item in items)
        {
            if (item.TryGetAdmissionWait(out long generation, out _))
            {
                deferred.Add((item, generation));
            }
        }

        foreach ((WorkItem item, long generation) in deferred)
        {
            item.SignalAdmissionAvailability(generation);
        }
    }

    private async Task ResumeAdmissionAfterBackoffAsync(WorkItem item, TimeSpan backoff)
    {
        if (!item.TryGetAdmissionWait(out long generation, out Task availability))
        {
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            _disposeCts.Token,
            item.Token);
        try
        {
            Task delay = _delayAsync(backoff, linked.Token);
            Task completed = await Task.WhenAny(delay, availability)
                .WaitAsync(linked.Token)
                .ConfigureAwait(false);
            if (ReferenceEquals(completed, availability))
            {
                linked.Cancel();
                try
                {
                    await delay.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                }
            }
            else
            {
                await delay.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // The production delay is Task.Delay; this guard keeps a host/test scheduler failure
            // from silently stranding the job forever.
            s_logger.LogError(ex, "Proxy admission retry delay failed; retrying immediately.");
        }

        if (item.TryResumeAdmission(generation))
        {
            _channel.Writer.TryWrite(item);
        }
    }

    private async Task RemoveAdmissionRetryWhenCompleteAsync(Task retry)
    {
        await retry.ConfigureAwait(false);
        lock (_lock)
        {
            _admissionRetryTasks.Remove(retry);
        }
    }
}
