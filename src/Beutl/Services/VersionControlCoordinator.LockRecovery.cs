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

        if (!_disposed)
        {
            _ = OfferLockRecoveryAsync(sender, lockInfo);
        }
    }

    // Offered under the operation gate, so one offer is shown at a time and the lock is never removed
    // while another operation runs Git. A project close cancels the offer.
    private async Task OfferLockRecoveryAsync(object? sender, RepositoryLockInfo lockInfo)
    {
        using RunningWork work = BeginWork();
        try
        {
            using OperationLease operation = await BeginOperationAsync(CancellationToken.None);
            if (sender is not IRepositoryLockRecoveryService recovery
                || !ReferenceEquals(CurrentService, sender)
                || !ReferenceEquals(recovery.RecoverableLock, lockInfo))
            {
                return;
            }

            if (lockInfo.RequiresManualRemoval)
            {
                // Consent cannot lead to a removal here, so go straight to the manual steps.
                ShowStaleLockManualRemovalWarning(lockInfo);
                return;
            }

            if (!await ConfirmRemoveStaleLockAsync(lockInfo, operation.CancellationToken)
                || _disposed
                || !ReferenceEquals(CurrentService, sender)
                || !ReferenceEquals(recovery.RecoverableLock, lockInfo))
            {
                return;
            }

            bool removed = await recovery.RemoveRecoverableLockAsync(
                lockInfo,
                operation.CancellationToken);
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
                ShowStaleLockManualRemovalWarning(lockInfo);
            }
        }
        catch (Exception ex)
            when (ex is OperationCanceledException or VersionControlLifecycleUnavailableException
                  || ex is ObjectDisposedException && _disposed)
        {
            // A close or the disposal ended the offer.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recover a stale Git repository lock.");
        }
    }

    private static void ShowStaleLockManualRemovalWarning(RepositoryLockInfo lockInfo)
    {
        NotificationService.ShowWarning(
            Strings.VersionControl,
            string.Format(
                CultureInfo.CurrentCulture,
                Strings.VersionControl_StaleLockManualRemovalRequiredFormat,
                lockInfo.LockPath));
    }
}
