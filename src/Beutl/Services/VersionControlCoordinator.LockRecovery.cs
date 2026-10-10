using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private void OnRecoverableLockAvailable(object? sender, RepositoryLockInfo lockInfo)
    {
        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }

            _lockRecoveryUsers++;
        }

        _ = RunLockRecoveryAsync(sender, lockInfo);
    }

    private async Task RunLockRecoveryAsync(object? sender, RepositoryLockInfo lockInfo)
    {
        try
        {
            if (_dispatcher.CheckAccess())
            {
                await OfferLockRecoveryAsync(sender, lockInfo);
            }
            else
            {
                await _dispatcher.InvokeAsync(
                    () => OfferLockRecoveryAsync(sender, lockInfo));
            }
        }
        finally
        {
            TaskCompletionSource? quiesced = null;
            lock (_stateGate)
            {
                _lockRecoveryUsers--;
                if (_lockRecoveryUsers == 0 && _disposed)
                {
                    quiesced = _lockRecoveryQuiesced;
                }
            }

            quiesced?.TrySetResult();
        }
    }

    private async Task OfferLockRecoveryAsync(object? sender, RepositoryLockInfo lockInfo)
    {
        bool gateEntered = false;
        try
        {
            await _lockRecoveryGate.WaitAsync(_lifetimeCancellation.Token);
            gateEntered = true;
            if (_disposed
                || sender is not IRepositoryLockRecoveryService recovery
                || !ReferenceEquals(CurrentService, sender)
                || !ReferenceEquals(recovery.RecoverableLock, lockInfo))
            {
                return;
            }

            if (lockInfo.RequiresManualRemoval)
            {
                // Consent cannot lead to a removal here, so go straight to the manual steps.
                await ShowStaleLockManualRemovalWarningAsync(lockInfo);
                return;
            }

            if (!await ConfirmRemoveStaleLockAsync(lockInfo, _lifetimeCancellation.Token)
                || _disposed
                || !ReferenceEquals(CurrentService, sender)
                || !ReferenceEquals(recovery.RecoverableLock, lockInfo))
            {
                return;
            }

            bool removed = await recovery.RemoveRecoverableLockAsync(
                lockInfo,
                _lifetimeCancellation.Token);
            if (removed)
            {
                _logger.LogWarning(
                    "Removed stale Git repository lock with user consent. Lock: {LockPath}, LastWriteTimeUtc: {LastWriteTimeUtc}",
                    lockInfo.LockPath,
                    lockInfo.LastWriteTimeUtc);
                await _dispatcher.InvokeAsync(() =>
                    NotificationService.ShowInformation(
                        Strings.VersionControl,
                        Strings.VersionControl_StaleLockRemoved));
            }
            else
            {
                await ShowStaleLockManualRemovalWarningAsync(lockInfo);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recover a stale Git repository lock.");
        }
        finally
        {
            if (gateEntered)
            {
                _lockRecoveryGate.Release();
            }
        }
    }

    private async Task ShowStaleLockManualRemovalWarningAsync(RepositoryLockInfo lockInfo)
    {
        await _dispatcher.InvokeAsync(() =>
            NotificationService.ShowWarning(
                Strings.VersionControl,
                string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.VersionControl_StaleLockManualRemovalRequiredFormat,
                    lockInfo.LockPath)));
    }
}
