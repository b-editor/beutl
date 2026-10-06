using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Editor.Components.VersionControl.Views;
using Beutl.Editor.Components.VersionControlTab.ViewModels;
using Beutl.Editor.VersionControl;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private void PublishNotification(Action notification, long? activationRevision = null)
    {
        lock (_stateGate)
        {
            if (_disposed && _lifecycleUsers == 0)
            {
                return;
            }

            if (!_dispatcher.CheckAccess())
            {
                _notificationUsers++;
                _ = PublishNotificationAsync(notification, activationRevision);
                return;
            }
        }

        TryPublishNotification(notification, activationRevision);
    }

    private async Task PublishNotificationAsync(Action notification, long? activationRevision)
    {
        try
        {
            await _dispatcher.InvokeAsync(() => TryPublishNotification(notification, activationRevision));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch a version-control notification.");
        }
        finally
        {
            TaskCompletionSource? quiesced = null;
            lock (_stateGate)
            {
                _notificationUsers--;
                if (_notificationUsers == 0 && _disposed)
                {
                    quiesced = _notificationsQuiesced;
                }
            }

            quiesced?.TrySetResult();
        }
    }

    private void TryPublishNotification(Action notification, long? activationRevision)
    {
        if (_dispatcher.CheckAccess())
        {
            try
            {
                if (activationRevision is { } expected)
                {
                    lock (_stateGate)
                    {
                        if (_disposed || expected != _latestActivationRevision)
                            return;
                        notification();
                    }
                }
                else
                {
                    notification();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish a version-control notification.");
            }
        }
        else
        {
            throw new InvalidOperationException(
                "Version-control notifications must be published on the captured dispatcher.");
        }
    }

    private Task<bool> ShowRestoreConfirmationAsync(
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            Strings.VersionControl_Restore,
            Strings.VersionControl_RestoreConfirmation,
            cancellationToken);
    }

    private Task<bool> ShowCloseWithoutSnapshotConfirmationAsync(CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            Strings.VersionControl_CloseWithoutSnapshot,
            Strings.VersionControl_CloseWithoutSnapshotConfirmation,
            cancellationToken);
    }

    private Task<bool> ShowSwitchBranchConfirmationAsync(
        string branchName,
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            Strings.VersionControl_SwitchBranch,
            CreateSwitchBranchConfirmation(
                branchName,
                CurrentService?.Repository?.IsNestedInForeignRepo == true),
            cancellationToken);
    }

    // A branch switch is repository-wide by design, so a project sharing someone else's repository
    // has to be told that the decision reaches past its own directory before it is taken.
    internal static string CreateSwitchBranchConfirmation(
        string branchName,
        bool isNestedInForeignRepo)
    {
        string confirmation = string.Format(
            CultureInfo.CurrentCulture,
            Strings.VersionControl_SwitchBranchConfirmation,
            branchName);
        return isNestedInForeignRepo
            ? $"{confirmation}\n\n{Strings.VersionControl_SwitchBranchEnclosingRepositoryNotice}"
            : confirmation;
    }

    private Task<bool> ShowPullConfirmationAsync(
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            Strings.VersionControl_Pull,
            Strings.VersionControl_PullConfirmation,
            cancellationToken);
    }

    private Task<bool> ShowPendingPullRecoveryConfirmationAsync(
        ProjectRecoveryInfo recovery,
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            Strings.VersionControl,
            string.Format(
                Strings.VersionControl_PendingPullRecoveryConfirmation,
                recovery.ProjectFileName,
                recovery.CreatedAt.ToLocalTime()),
            cancellationToken);
    }

    private async Task<bool> ShowAdoptExistingRepositoryConfirmationAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetimeCancellation.Token);
        CancellationToken token = cancellation.Token;
        var request = new RepositoryAdoptionRequest(repository);
        using CancellationTokenRegistration registration = token.Register(() => request.Cancel(token));
        VersionControlTabViewModel? presentedTab = null;
        using IDisposable selection = _editorService.SelectedTabItem.Subscribe(
            _ => _dispatcher.Post(ShowConfirmationTab));
        try
        {
            await _dispatcher.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                RepositoryAdoptionRequest? previous;
                lock (_stateGate)
                {
                    if (_operationCloseBarrierActive)
                    {
                        request.Respond(false);
                        return;
                    }
                    previous = _pendingRepositoryAdoption;
                    _pendingRepositoryAdoption = request;
                }
                previous?.Respond(false);
                RepositoryAdoptionChanged?.Invoke(this, EventArgs.Empty);
                ShowConfirmationTab();
            }, DispatcherPriority.Normal, token);
            return await request.Completion;
        }
        finally
        {
            if (presentedTab is not null)
                presentedTab.Disposed -= OnPresentedTabDisposed;
            request.Respond(false);
            await _dispatcher.InvokeAsync(() =>
            {
                lock (_stateGate)
                {
                    if (!ReferenceEquals(_pendingRepositoryAdoption, request)) return;
                    _pendingRepositoryAdoption = null;
                }
                RepositoryAdoptionChanged?.Invoke(this, EventArgs.Empty);
            });
        }

        void ShowConfirmationTab()
        {
            if (request.Completion.IsCompleted || token.IsCancellationRequested
                || !ReferenceEquals(PendingRepositoryAdoption, request))
            {
                return;
            }

            if (_editorService.SelectedTabItem.Value is not { } selected
                || !_editorService.TabItems.Contains(selected)
                || selected.Context.Value is not EditViewModel editor
                || _projectService.CurrentProject.Value is not { } project
                || !project.Items.Contains(editor.Scene))
            {
                request.Respond(false);
                return;
            }

            VersionControlTabViewModel? existing = editor.FindToolTab<VersionControlTabViewModel>();
            if (existing is not null && ReferenceEquals(existing, presentedTab))
                return;

            var tab = existing ?? new VersionControlTabViewModel(VersionControlTabExtension.Instance, editor);
            if (!editor.OpenToolTab(tab))
            {
                request.Respond(false);
                if (existing is null)
                    tab.Dispose();
                return;
            }

            if (presentedTab is not null)
                presentedTab.Disposed -= OnPresentedTabDisposed;
            presentedTab = tab;
            presentedTab.Disposed += OnPresentedTabDisposed;
        }

        void OnPresentedTabDisposed(object? sender, EventArgs e) => request.Respond(false);
    }

    private Task<bool> ShowEnclosingRepositoryConfirmationAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            Strings.VersionControl,
            $"{Strings.VersionControl_EnclosingRepositoryFound}\n\n{repository.RepoRoot}",
            cancellationToken);
    }

    private Task<bool> ShowUntrackReservedPathsConfirmationAsync(
        IReadOnlyList<string> reservedPaths,
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            Strings.VersionControl,
            $"{Strings.VersionControl_UntrackReservedPathsConfirmation}\n\n{string.Join('\n', reservedPaths)}",
            cancellationToken);
    }

    private Task<bool> ShowStaleLockConfirmationAsync(
        RepositoryLockInfo lockInfo,
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            Strings.VersionControl,
            $"{Strings.VersionControl_StaleLockConfirmation}\n\n{lockInfo.LockPath}",
            cancellationToken);
    }

    // The backend asks from its own Git continuation, but the identity prompt is a flyout that has to be
    // created on the UI thread, like the one a manual commit shows.
    private Task<GitIdentity?> RequestIdentityForSnapshotAsync(CancellationToken cancellationToken)
    {
        return _dispatcher.CheckAccess()
            ? RequestIdentityAsync(cancellationToken)
            : _dispatcher.InvokeAsync(
                () => RequestIdentityAsync(cancellationToken),
                DispatcherPriority.Normal);
    }

    private async Task ShowConflictMarkerWarningAsync(string markerFile)
    {
        await _dispatcher.InvokeAsync(
            () => NotificationService.ShowWarning(
                Strings.VersionControl_ConflictMarkerWarningTitle,
                string.Format(
                    Strings.VersionControl_ConflictMarkerWarning,
                    markerFile)));
    }

    private async Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        VersionControlPickerFlyout? flyout = null;
        Task<bool>? confirmation = null;
        await _dispatcher.InvokeAsync(() =>
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
            () => _dispatcher.Post(flyout.Hide));
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

    private async Task ShowPolicyNoticeAsync(
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
            _ => throw new ArgumentOutOfRangeException(nameof(notice)),
        };

        await _dispatcher.InvokeAsync(() =>
            NotificationService.ShowWarning(Strings.VersionControl, message));
    }
}
