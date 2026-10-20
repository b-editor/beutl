using Beutl.Api.Services;
using Beutl.Services;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels.Tools;

public sealed partial class AiJobCenterViewModel
{
    private AiJobConfirmationAction _confirmationAction;

    private IAiJobRetryHandler? _confirmationHandler;
    private Task<AiJobRetryPreflight>? _confirmationPreflightTask;
    private CancellationTokenSource? _confirmationPreflightCts;
    private long _confirmationRevision;

    internal async Task RequestRetryConfirmationAsync(AiJobItemViewModel item)
    {
        using AsyncOperationLifetime.Operation? lifetimeOperation = _operations.TryEnter();
        if (lifetimeOperation is null)
            return;
        ArgumentNullException.ThrowIfNull(item);
        if (!item.CanRetry || IsDisposed)
            return;

        ReleaseConfirmationResources();
        long revision = Interlocked.Increment(ref _confirmationRevision);
        _confirmationAction = AiJobConfirmationAction.Retry;
        _confirmationItem = item;
        ConfirmationTitle.Value = Strings.AiJobCenter_RetryTitle;
        ConfirmationMessage.Value = Strings.AiJobCenter_CheckingRetryCost;
        ConfirmationActionText.Value = Strings.AiJobCenter_Retry;
        CanConfirm.Value = false;
        IsConfirmationLoading.Value = true;
        IsConfirmationOpen.Value = true;

        IAiJobRetryHandler? retryHandler = null;
        Task<AiJobRetryPreflight>? preflightTask = null;
        CancellationTokenSource? preflightCts = null;
        try
        {
            if (!_jobKinds.TryGetRetryHandler(item.Job.Kind, out retryHandler))
            {
                SetOperationError(Strings.AiPricingUnavailable);
                return;
            }

            _confirmationHandler = retryHandler;

            AiJobStatusSemantics status = _jobKinds.GetStatus(item.Job);
            if (!retryHandler.CanRetry(item.Job, status))
            {
                SetOperationError(Strings.AiPricingUnavailable);
                ReleaseConfirmationResources();
                return;
            }

            preflightCts = CancellationTokenSource.CreateLinkedTokenSource(
                lifetimeOperation.CancellationToken);
            _confirmationPreflightCts = preflightCts;
            preflightTask = retryHandler.GetPreflightAsync(
                item.Job,
                preflightCts.Token).AsTask();
            _confirmationPreflightTask = preflightTask;
            AiJobRetryPreflight estimate = await preflightTask;
            if (ReferenceEquals(_confirmationPreflightTask, preflightTask))
            {
                _confirmationPreflightTask = null;
                if (ReferenceEquals(_confirmationPreflightCts, preflightCts))
                {
                    _confirmationPreflightCts = null;
                    preflightCts.Dispose();
                }
            }
            if (!TryPublishConfirmationPreflight(revision, retryHandler, estimate))
                return;
            if (!estimate.CanSubmit)
            {
                ReleaseConfirmationResources();
                retryHandler = null;
            }
        }
        catch (AuthenticationRequiredException)
        {
            FailConfirmation(revision, retryHandler, Strings.AiAuthenticationRequired);
        }
        catch (AiJobRetryPreparationRejectedException)
        {
            FailConfirmation(revision, retryHandler, Strings.AiResultUnavailable);
        }
        catch (AiJobRetryPreparationUnavailableException)
        {
            FailConfirmation(revision, retryHandler, Strings.AiPricingUnavailable);
        }
        catch (OperationCanceledException) when (preflightCts?.IsCancellationRequested == true)
        {
            if (IsCurrentConfirmation(revision, retryHandler))
                ReleaseConfirmationResources();
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            if (IsCurrentConfirmation(revision, retryHandler))
                ReleaseConfirmationResources();
        }
        catch (Exception ex)
        {
            if (IsCurrentConfirmation(revision, retryHandler))
            {
                _logger.LogDebug(ex, "Failed to refresh authoritative pricing before retrying AI job {JobId}", item.Id);
                SetOperationError(Strings.AiPricingUnavailable);
                ReleaseConfirmationResources();
            }
        }
        finally
        {
            CompleteConfirmationLoading(revision);
        }
    }

    private bool TryPublishConfirmationPreflight(
        long revision,
        IAiJobRetryHandler handler,
        AiJobRetryPreflight estimate)
    {
        lock (_lifetimeGate)
        {
            if (revision != _confirmationRevision
                || _isDisposed
                || !ReferenceEquals(_confirmationHandler, handler))
                return false;

            ConfirmationMessage.Value = estimate.IsAvailable
                ? string.Join(
                    Environment.NewLine,
                    Strings.AiJobCenter_RetryConfirmation,
                    estimate.Explanation)
                : estimate.Explanation;
            CanConfirm.Value = estimate.CanSubmit;
            return true;
        }
    }

    private void CompleteConfirmationLoading(long revision)
    {
        lock (_lifetimeGate)
        {
            if (revision == _confirmationRevision && !_isDisposed)
                IsConfirmationLoading.Value = false;
        }
    }

    private bool IsCurrentConfirmation(long revision, IAiJobRetryHandler? handler)
        => revision == Volatile.Read(ref _confirmationRevision)
            && !IsDisposed
            && (handler is null || ReferenceEquals(_confirmationHandler, handler));

    // Says why the confirmation on show cannot go ahead, unless another has replaced it.
    private void FailConfirmation(long revision, IAiJobRetryHandler? handler, string error)
    {
        if (IsCurrentConfirmation(revision, handler))
        {
            SetOperationError(error);
            ReleaseConfirmationResources();
        }
    }

    internal void RequestDeleteConfirmation(AiJobItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.CanDelete || IsDisposed)
            return;

        ReleaseConfirmationResources();
        Interlocked.Increment(ref _confirmationRevision);
        _confirmationAction = AiJobConfirmationAction.Delete;
        _confirmationItem = item;
        ConfirmationTitle.Value = string.IsNullOrWhiteSpace(item.Summary)
            ? Strings.AiJobCenter_DeleteTitle
            : string.Format(Strings.AiJobCenter_DeleteTitleFormat, Shorten(item.Summary));
        ConfirmationMessage.Value = Strings.AiJobCenter_DeleteConfirmation;
        ConfirmationActionText.Value = Strings.Delete;
        CanConfirm.Value = true;
        IsConfirmationLoading.Value = false;
        IsConfirmationOpen.Value = true;
    }

    private static string Shorten(string text, int maximumLength = 48)
    {
        string normalized = text.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength].TrimEnd() + "…";
    }

    private void ReleaseConfirmationResources()
    {
        Task<AiJobRetryPreflight>? preflight = Interlocked.Exchange(ref _confirmationPreflightTask, null);
        CancellationTokenSource? preflightCts = Interlocked.Exchange(
            ref _confirmationPreflightCts,
            null);
        _confirmationHandler = null;
        CancelPreflight(preflightCts);
        if (preflight is { IsCompleted: false })
            _ = DisposeAfterPreflightAsync(preflight, preflightCts);
        else
            preflightCts?.Dispose();
    }

    // The preflight may still use its token until it completes.
    private async Task DisposeAfterPreflightAsync(
        Task<AiJobRetryPreflight> preflight,
        CancellationTokenSource? preflightCts)
    {
        using AsyncOperationLifetime.Operation? lifetimeOperation = _operations.TryEnter();
        try
        {
            await preflight;
        }
        catch
        {
        }
        preflightCts?.Dispose();
    }

    private void CancelPreflight(CancellationTokenSource? preflightCts)
    {
        if (preflightCts is null)
            return;

        try
        {
            preflightCts.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AI retry preflight cancellation callback failed");
        }
    }

    private void ClearConfirmationState()
    {
        Interlocked.Increment(ref _confirmationRevision);
        _confirmationAction = AiJobConfirmationAction.None;
        _confirmationItem = null;
        IsConfirmationOpen.Value = false;
        IsConfirmationLoading.Value = false;
        CanConfirm.Value = false;
    }

    internal void CancelConfirmation()
    {
        ClearConfirmationState();
        ReleaseConfirmationResources();
    }

    internal async Task ConfirmPendingActionAsync()
    {
        using AsyncOperationLifetime.Operation? lifetimeOperation = _operations.TryEnter();
        if (lifetimeOperation is null)
            return;
        if (!CanConfirm.Value || _confirmationItem is not { } item)
            return;

        AiJobConfirmationAction action = _confirmationAction;
        IAiJobRetryHandler? handler = _confirmationHandler;
        _confirmationHandler = null;
        ClearConfirmationState();
        switch (action)
        {
            case AiJobConfirmationAction.Retry:
                // A confirmable retry always holds the handler its preflight took.
                await RetryJobAsync(item, handler!);
                break;
            case AiJobConfirmationAction.Delete:
                await DeleteJobAsync(item);
                break;
        }
    }
}

internal enum AiJobConfirmationAction
{
    None,
    Retry,
    Delete,
}
