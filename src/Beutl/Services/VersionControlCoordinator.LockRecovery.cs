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
            _ = WarnAboutStaleLockAsync(sender, lockInfo);
        }
    }

    // Shown under the operation gate: one warning at a time, after the operation that hit the lock.
    private async Task WarnAboutStaleLockAsync(object? sender, RepositoryLockInfo lockInfo)
    {
        using RunningWork work = BeginWork();
        try
        {
            using OperationLease operation = await BeginOperationAsync(CancellationToken.None);
            if (sender is IRepositoryLockRecoveryService recovery
                && ReferenceEquals(CurrentService, sender)
                && ReferenceEquals(recovery.RecoverableLock, lockInfo))
            {
                ShowStaleLockManualRemovalWarning(lockInfo);
            }
        }
        catch (Exception ex)
            when (ex is OperationCanceledException or VersionControlLifecycleUnavailableException
                  || ex is ObjectDisposedException && _disposed)
        {
            // A close or the disposal ended the warning.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to warn about a stale Git repository lock.");
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
