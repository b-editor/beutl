using Beutl.Api.Services;
using Beutl.Editor.Components.Helpers;
using Beutl.Services;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.AiWorkspaceTab.ViewModels;

public sealed partial class AiJobCenterViewModel
{
    private AiJobConfirmationAction _confirmationAction;
    private long _confirmationRevision;

    internal async Task RequestRetryConfirmationAsync(AiJobItemViewModel item)
    {
        using AsyncOperationLifetime.Operation? lifetimeOperation = _operations.TryEnter();
        if (lifetimeOperation is null)
            return;
        ArgumentNullException.ThrowIfNull(item);
        if (!item.CanRetry || IsDisposed)
            return;

        long revision = Interlocked.Increment(ref _confirmationRevision);
        _confirmationAction = AiJobConfirmationAction.Retry;
        _confirmationItem = item;
        ConfirmationTitle.Value = Strings.AiJobCenter_RetryTitle;
        ConfirmationMessage.Value = Strings.AiJobCenter_CheckingRetryCost;
        ConfirmationActionText.Value = Strings.AiJobCenter_Retry;
        CanConfirm.Value = false;
        IsConfirmationLoading.Value = true;
        IsConfirmationOpen.Value = true;

        try
        {
            AiEntitlements? entitlements = await _entitlements.RefreshAsync(lifetimeOperation.CancellationToken);
            (string message, bool canSubmit) = entitlements switch
            {
                null => (Strings.AiPricingUnavailable, false),
                { CanUseAi: false } => (string.Join(Environment.NewLine, Strings.AiJobCenter_RetryConfirmation, Strings.AiProRequired), false),
                // The server refuses a retry that neither the allowance nor purchased credits can pay for.
                { Balance: { MonthlyUsage.IsExhausted: true, AdditionalCredits: <= 0 } } =>
                    (string.Join(Environment.NewLine, Strings.AiJobCenter_RetryConfirmation, Strings.AiEstimatedUsageInsufficient), false),
                _ => (string.Join(
                    Environment.NewLine,
                    Strings.AiJobCenter_RetryConfirmation,
                    entitlements.Balance.MonthlyUsage.IsExhausted
                        ? Strings.AiEstimatedUsageTopUp
                        : Strings.AiEstimatedUsageMonthly), true),
            };
            if (IsCurrentConfirmation(revision))
            {
                ConfirmationMessage.Value = message;
                CanConfirm.Value = canSubmit;
            }
        }
        catch (AuthenticationRequiredException)
        {
            if (IsCurrentConfirmation(revision))
                SetOperationError(Strings.AiAuthenticationRequired);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (IsCurrentConfirmation(revision))
            {
                _logger.LogDebug(ex, "Failed to refresh the usage before retrying AI job {JobId}", item.Id);
                SetOperationError(Strings.AiPricingUnavailable);
            }
        }
        finally
        {
            CompleteConfirmationLoading(revision);
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

    private bool IsCurrentConfirmation(long revision)
        => revision == Volatile.Read(ref _confirmationRevision) && !IsDisposed;

    internal void RequestDeleteConfirmation(AiJobItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.CanDelete || IsDisposed)
            return;

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

    private void ClearConfirmationState()
    {
        Interlocked.Increment(ref _confirmationRevision);
        _confirmationAction = AiJobConfirmationAction.None;
        _confirmationItem = null;
        IsConfirmationOpen.Value = false;
        IsConfirmationLoading.Value = false;
        CanConfirm.Value = false;
    }

    internal void CancelConfirmation() => ClearConfirmationState();

    internal async Task ConfirmPendingActionAsync()
    {
        using AsyncOperationLifetime.Operation? lifetimeOperation = _operations.TryEnter();
        if (lifetimeOperation is null)
            return;
        if (!CanConfirm.Value || _confirmationItem is not { } item)
            return;

        AiJobConfirmationAction action = _confirmationAction;
        ClearConfirmationState();
        switch (action)
        {
            case AiJobConfirmationAction.Retry:
                await RetryJobAsync(item);
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
