using Beutl.Api.Services;
using Beutl.Services;
using Beutl.Services.AI;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

internal sealed partial class AiVideoGenerationDialogViewModel
{
    internal Func<TimeSpan, CancellationToken, Task> PollDelayAsync { get; set; } = Task.Delay;

    internal TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    internal TimeSpan MaximumTransientPollDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Waits for the job to finish, showing what it is doing.</summary>
    /// <returns>
    /// True once the server has settled the job — the clip is in hand, or the
    /// job failed and was refunded. False while its outcome is still unknown to
    /// this client, which is what makes the request worth repeating as itself.
    /// </returns>
    private async Task<bool> PollJobAsync(
        AiJobId jobId,
        IdentityOperationLifetime.Operation operation,
        AiVideoResultSnapshot pendingSnapshot)
    {
        CancellationTokenSource pollingCts;
        lock (_lifetimeGate)
        {
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
            pollingCts = CancellationTokenSource.CreateLinkedTokenSource(operation.CancellationToken);
            _pollingCts = pollingCts;
        }
        CancellationToken token = pollingCts.Token;
        operation.TryPublish(() => IsWaitingForJob.Value = true);

        try
        {
            (AiVideoJob job, AiJobStatusSemantics status) = await AiVideoJobWaiter.WaitAsync(
                _videos, _jobKinds, jobId,
                () => operation.TryPublish(() => StatusText.Value = Strings.AiVideoProcessing),
                PollInterval, MaximumTransientPollDelay, PollDelayAsync, token);
            if (status.Outcome == AiJobOutcomes.Succeeded)
            {
                if (job.ContentUri is not { } contentUri)
                {
                    throw new InvalidOperationException("A successful video job did not provide content.");
                }

                string? localPath = await DownloadVideoAsync(
                    contentUri,
                    job.ContentMetadata,
                    operation,
                    token);
                if (localPath is null)
                    return false;

                operation.TryPublish(() =>
                {
                    string? previousPath = ResultVideoPath.Value;
                    StatusText.Value = Strings.AiVideoCompleted;
                    ResultVideoPath.Value = localPath;
                    _resultSnapshot = pendingSnapshot;
                    if (!string.Equals(previousPath, localPath, StringComparison.Ordinal))
                    {
                        RequestTemporaryFileDeletion(previousPath);
                    }
                });
                return true;
            }
            if (status.IsTerminal)
            {
                operation.TryPublish(() =>
                {
                    StatusText.Value = Strings.AiVideoFailed;
                    Error.Value = AiErrorMessage.Localize(job.Error) ?? Strings.AiProviderError;
                });
                return true;
            }

            if (!status.ShouldPoll)
            {
                // An unknown status is not a terminal outcome. The server may
                // have introduced it during a rolling upgrade, so keep the
                // request recoverable and require a later history refresh.
                operation.TryPublish(() =>
                {
                    StatusText.Value = Strings.AiResultUnavailable;
                    Error.Value = Strings.AiResultUnavailable;
                });
                return false;
            }

            return false;
        }
        catch (OperationCanceledException) when (
            pollingCts.IsCancellationRequested
            && !operation.CancellationToken.IsCancellationRequested)
        {
            await RefreshJobHistoryAfterLocalStopAsync();
            operation.TryPublish(() => StatusText.Value = Strings.AiVideoWaitStopped);
        }
        finally
        {
            operation.TryPublish(() => IsWaitingForJob.Value = false);
            lock (_lifetimeGate)
            {
                if (ReferenceEquals(_pollingCts, pollingCts))
                    _pollingCts = null;
            }

            pollingCts.Dispose();
        }

        // Stopped waiting rather than heard an answer: the job is still the
        // server's, and the key that created it is still the way back to it.
        return false;
    }

    private async Task<string?> DownloadVideoAsync(
        Uri contentUri,
        AiContentMetadata? declaredMetadata,
        IdentityOperationLifetime.Operation operation,
        CancellationToken cancellationToken)
    {
        string? filePath = null;
        try
        {
            filePath = await AiVideoResultDownload.DownloadAsync(
                _content, contentUri, declaredMetadata, cancellationToken);

            if (!operation.TryPublish(() =>
                {
                    lock (_lifetimeGate)
                    {
                        _temporaryFiles.Add(filePath);
                    }
                }))
            {
                DeleteTemporaryFile(filePath);
                return null;
            }

            return filePath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (filePath is not null)
            {
                DeleteTemporaryFile(filePath);
            }

            throw;
        }
        // The clip was rendered and charged for; only fetching it failed, and it
        // is still waiting in the job history.
        catch (AiContentUnavailableException ex)
        {
            if (filePath is not null)
            {
                DeleteTemporaryFile(filePath);
            }
            _logger.LogError(ex, "Failed to download the AI video.");
            operation.TryPublish(() => Error.Value = Strings.AiResultDownloadFailed);
            return null;
        }
        catch (Exception ex)
        {
            if (filePath is not null)
            {
                DeleteTemporaryFile(filePath);
            }
            _logger.LogError(ex, "Failed to download the AI video.");
            operation.TryPublish(() => Error.Value = Strings.AiUnexpectedError);
            return null;
        }
    }

    // A job the server already accepted keeps running and lands in the job centre,
    // so stopping means stopping the wait. Before that there is nothing to keep and
    // the request itself goes.
    private void StopGeneratingCore()
    {
        CancellationTokenSource? polling;
        lock (_lifetimeGate)
        {
            polling = _pollingCts;
        }

        if (polling is { IsCancellationRequested: false })
        {
            polling.Cancel();
            return;
        }

        _runningRequest?.Cancel();
    }

    private async Task RefreshJobHistoryAfterLocalStopAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _jobMonitor.RefreshAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed to refresh AI job history after stopping a local wait.");
        }
        catch (OperationCanceledException)
        {
        }
    }
}
