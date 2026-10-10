using Avalonia.Threading;
using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    // Raised by the backend from its own Git continuation.
    private void OnRecoverableLockAvailable(object? sender, RepositoryLockInfo lockInfo)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() => OnRecoverableLockAvailable(sender, lockInfo), DispatcherPriority.Normal);
            return;
        }

        if (_disposed)
        {
            return;
        }

        _lockRecoveryUsers++;
        _ = RunLockRecoveryAsync(sender, lockInfo);
    }

    private async Task RunLockRecoveryAsync(object? sender, RepositoryLockInfo lockInfo)
    {
        try
        {
            await OfferLockRecoveryAsync(sender, lockInfo);
        }
        finally
        {
            _lockRecoveryUsers--;
            if (_lockRecoveryUsers == 0 && _disposed)
            {
                _lockRecoveryQuiesced?.TrySetResult();
            }
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
                NotificationService.ShowInformation(
                    Strings.VersionControl,
                    Strings.VersionControl_StaleLockRemoved);
            }
            else
            {
                NotificationService.ShowWarning(
                    Strings.VersionControl,
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Strings.VersionControl_StaleLockManualRemovalRequiredFormat,
                        lockInfo.LockPath));
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
}
