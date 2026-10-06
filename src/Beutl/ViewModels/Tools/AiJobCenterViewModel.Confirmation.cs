using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Editor.Services.AI;
using Beutl.Editor.Services.Captions;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.AI;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using SkiaSharp;

namespace Beutl.ViewModels.Tools;

public sealed partial class AiJobCenterViewModel
{
    private AiJobConfirmationAction _confirmationAction;

    private IAiJobRetryHandlerLease? _confirmationLease;
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
        _confirmationLease = null;
        _confirmationHandler = null;
        ConfirmationTitle.Value = Strings.AiJobCenter_RetryTitle;
        ConfirmationMessage.Value = Strings.AiJobCenter_CheckingRetryCost;
        ConfirmationActionText.Value = Strings.AiJobCenter_Retry;
        CanConfirm.Value = false;
        IsConfirmationLoading.Value = true;
        IsConfirmationOpen.Value = true;

        IAiJobRetryHandlerLease? lease = null;
        Task<AiJobRetryPreflight>? preflightTask = null;
        CancellationTokenSource? preflightCts = null;
        try
        {
            if (!_jobKinds.TryAcquireRetryHandler(item.Job.Kind, out lease))
            {
                SetOperationError(Strings.AiPricingUnavailable);
                return;
            }

            _confirmationLease = lease;

            AiJobStatusSemantics status = _jobKinds.GetStatus(item.Job);
            IAiJobRetryHandler retryHandler = lease.Handler;
            if (!retryHandler.CanRetry(item.Job, status))
            {
                SetOperationError(Strings.AiPricingUnavailable);
                ReleaseConfirmationResources();
                return;
            }

            _confirmationHandler = retryHandler;
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
            if (!TryPublishConfirmationPreflight(revision, lease, estimate))
                return;
            if (!estimate.CanSubmit)
            {
                ReleaseConfirmationResources();
                lease = null;
            }
        }
        catch (AuthenticationRequiredException)
        {
            FailConfirmation(revision, lease, Strings.AiAuthenticationRequired);
        }
        catch (AiJobRetryPreparationRejectedException)
        {
            FailConfirmation(revision, lease, Strings.AiResultUnavailable);
        }
        catch (AiJobRetryPreparationUnavailableException)
        {
            FailConfirmation(revision, lease, Strings.AiPricingUnavailable);
        }
        catch (OperationCanceledException) when (preflightCts?.IsCancellationRequested == true)
        {
            if (IsCurrentConfirmation(revision, lease))
                ReleaseConfirmationResources();
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            if (IsCurrentConfirmation(revision, lease))
                ReleaseConfirmationResources();
        }
        catch (Exception ex)
        {
            if (IsCurrentConfirmation(revision, lease))
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
        IAiJobRetryHandlerLease lease,
        AiJobRetryPreflight estimate)
    {
        lock (_lifetimeGate)
        {
            if (revision != _confirmationRevision
                || _isDisposed
                || !ReferenceEquals(_confirmationLease, lease))
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

    private bool IsCurrentConfirmation(long revision, IAiJobRetryHandlerLease? lease)
        => revision == Volatile.Read(ref _confirmationRevision)
            && !IsDisposed
            && (lease is null || ReferenceEquals(_confirmationLease, lease));

    // Says why the confirmation on show cannot go ahead, unless another has replaced it.
    private void FailConfirmation(long revision, IAiJobRetryHandlerLease? lease, string error)
    {
        if (IsCurrentConfirmation(revision, lease))
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
        IAiJobRetryHandlerLease? lease = Interlocked.Exchange(ref _confirmationLease, null);
        Task<AiJobRetryPreflight>? preflight = Interlocked.Exchange(ref _confirmationPreflightTask, null);
        CancellationTokenSource? preflightCts = Interlocked.Exchange(
            ref _confirmationPreflightCts,
            null);
        _confirmationHandler = null;
        CancelPreflight(preflightCts);
        if (lease is not null)
        {
            if (preflight is { IsCompleted: false })
            {
                _ = DisposeLeaseAfterPreflightAsync(preflight, lease, preflightCts);
            }
            else
            {
                DisposeConfirmationLease(lease);
                preflightCts?.Dispose();
            }
        }
        else
        {
            preflightCts?.Dispose();
        }
    }

    private async Task DisposeLeaseAfterPreflightAsync(
        Task<AiJobRetryPreflight> preflight,
        IAiJobRetryHandlerLease lease,
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
        DisposeConfirmationLease(lease);
        preflightCts?.Dispose();
    }

    private void DisposeConfirmationLease(IAiJobRetryHandlerLease lease)
    {
        try
        {
            lease.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to release an AI retry confirmation lease");
        }
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
        IAiJobRetryHandlerLease? lease = Interlocked.Exchange(ref _confirmationLease, null);
        IAiJobRetryHandler? handler = _confirmationHandler;
        _confirmationHandler = null;
        ClearConfirmationState();
        switch (action)
        {
            case AiJobConfirmationAction.Retry:
                await RetryJobAsync(item, lease, handler);
                break;
            case AiJobConfirmationAction.Delete:
                lease?.Dispose();
                await DeleteJobAsync(item);
                break;
            default:
                lease?.Dispose();
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
