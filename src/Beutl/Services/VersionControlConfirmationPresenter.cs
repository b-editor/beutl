using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Editor.Components.VersionControl.Views;
using Beutl.Editor.VersionControl;

namespace Beutl.Services;

internal sealed class VersionControlConfirmationPresenter(Dispatcher dispatcher)
{
    internal Task<bool> ShowRestoreConfirmationAsync(
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            Strings.VersionControl_Restore,
            Strings.VersionControl_RestoreConfirmation,
            cancellationToken);
    }

    internal Task<bool> ShowCloseWithoutSnapshotConfirmationAsync(CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            Strings.VersionControl_CloseWithoutSnapshot,
            Strings.VersionControl_CloseWithoutSnapshotConfirmation,
            cancellationToken);
    }

    internal Task<bool> ShowPullConfirmationAsync(
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            Strings.VersionControl_Pull,
            Strings.VersionControl_PullConfirmation,
            cancellationToken);
    }

    internal Task<bool> ShowEnclosingRepositoryConfirmationAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            Strings.VersionControl,
            $"{Strings.VersionControl_EnclosingRepositoryFound}\n\n{repository.RepoRoot}",
            cancellationToken);
    }

    internal Task<bool> ShowUntrackReservedPathsConfirmationAsync(
        IReadOnlyList<string> reservedPaths,
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            Strings.VersionControl,
            $"{Strings.VersionControl_UntrackReservedPathsConfirmation}\n\n{string.Join('\n', reservedPaths)}",
            cancellationToken);
    }

    internal Task<bool> ShowStaleLockConfirmationAsync(
        RepositoryLockInfo lockInfo,
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            Strings.VersionControl,
            $"{Strings.VersionControl_StaleLockConfirmation}\n\n{lockInfo.LockPath}",
            cancellationToken);
    }

    internal async Task ShowConflictMarkerWarningAsync(string markerFile)
    {
        await dispatcher.InvokeAsync(
            () => NotificationService.ShowWarning(
                Strings.VersionControl_ConflictMarkerWarningTitle,
                string.Format(
                    Strings.VersionControl_ConflictMarkerWarning,
                    markerFile)));
    }

    internal async Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        VersionControlPickerFlyout? flyout = null;
        Task<bool>? confirmation = null;
        await dispatcher.InvokeAsync(() =>
        {
            if (GetFlyoutAnchor() is not { } anchor)
            {
                return;
            }

            flyout = new VersionControlPickerFlyout();
            confirmation = flyout.ShowConfirmationAsync(anchor, title, message);
        });

        if (flyout is null || confirmation is null)
        {
            return false;
        }

        using CancellationTokenRegistration registration = cancellationToken.Register(
            () => dispatcher.Post(flyout.Hide));
        return await confirmation.WaitAsync(cancellationToken);
    }

    private static Control? GetFlyoutAnchor()
    {
        if (Application.Current?.ApplicationLifetime
                is not IClassicDesktopStyleApplicationLifetime
                {
                    MainWindow: { } mainWindow,
                })
        {
            return null;
        }

        Control? focused = mainWindow.FocusManager?.GetFocusedElement() as Control;
        return focused?.IsAttachedToVisualTree() == true
            ? focused
            : mainWindow;
    }

    // A truncated list ends in "…" even when it names fewer paths than fit, since more exist.
    private static string FormatPathList(IReadOnlyList<string> paths, bool truncated)
    {
        const int MaxListedPaths = 5;
        string listed = string.Join(", ", paths.Take(MaxListedPaths));
        if (paths.Count <= MaxListedPaths && !truncated)
        {
            return listed;
        }

        return listed.Length == 0 ? "…" : listed + ", …";
    }

    internal async Task ShowPolicyNoticeAsync(
        VersionControlPolicyNotice notice,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string message = notice switch
        {
            VersionControlPolicyNotice.LfsRemoteQuota
                => Strings.VersionControl_LfsQuotaNotice,
            VersionControlPolicyNotice.LfsInstallFailed
                => Strings.VersionControl_LfsInstallFailedNotice,
            VersionControlPolicyNotice.LargeMediaWithoutLfs largeMedia
                => string.Format(
                    Strings.VersionControl_LargeMediaWarningFormat,
                    largeMedia.Path),
            VersionControlPolicyNotice.MissingIdentity
                => Strings.VersionControl_MissingIdentityNotice,
            VersionControlPolicyNotice.IgnoredProjectFiles ignored
                => string.Format(
                    Strings.VersionControl_IgnoredProjectFilesNoticeFormat,
                    FormatPathList(ignored.Paths, ignored.Truncated)),
            _ => throw new ArgumentOutOfRangeException(nameof(notice)),
        };

        await dispatcher.InvokeAsync(() =>
            NotificationService.ShowWarning(Strings.VersionControl, message));
    }
}
