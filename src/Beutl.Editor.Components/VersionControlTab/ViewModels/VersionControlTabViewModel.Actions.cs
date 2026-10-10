using Beutl.Editor.Components.VersionControl.ViewModels;
using Beutl.Editor.VersionControl;
using Beutl.Services;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.VersionControlTab.ViewModels;

internal partial class VersionControlTabViewModel
{
    public async Task EnableVersionControlAsync()
    {
        if (!CanEnableVersionControl.Value || IsEnablingVersionControl.Value)
        {
            return;
        }

        // Initialization saves the project, runs git init and writes the first commit, so the panel
        // has to stay in a running state until the shell flow reports back.
        IsEnablingVersionControl.Value = true;
        try
        {
            await RequestEnableVersionControlAsync();
        }
        finally
        {
            IsEnablingVersionControl.Value = false;
        }

        if (_service?.Repository is not null)
        {
            IsTracked.Value = true;
        }
    }

    public async Task DownloadGitAsync()
    {
        if (IsUnavailable.Value)
        {
            await LaunchUriAsync(s_gitDownloadsUri);
        }
    }

    public async Task CommitManualAsync()
    {
        string submittedDraft = CommitMessage.Value;
        if (_versionControlCoordinator is null || string.IsNullOrWhiteSpace(submittedDraft))
        {
            return;
        }

        string submittedMessage = submittedDraft.Trim();

        try
        {
            CommitResult result = await _versionControlCoordinator.CommitManualAsync(
                submittedMessage,
                CancellationToken.None);
            switch (result)
            {
                case CommitResult.NoChanges:
                    StatusMessage.Value = Strings.VersionControl_NothingToCommit;
                    break;
                case CommitResult.Committed:
                    if (string.Equals(
                            CommitMessage.Value,
                            submittedDraft,
                            StringComparison.Ordinal))
                    {
                        CommitMessage.Value = string.Empty;
                    }

                    StatusMessage.Value = Strings.VersionControl_CommitCreated;
                    break;
            }
        }
        catch (GitIdentityRequiredException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The manual commit command failed.");
            NotificationService.ShowError(Strings.VersionControl_ErrorTitle, ex.Message);
        }
    }

    internal Task<bool> RestoreAsync(CommitInfo commit)
    {
        return RunRestoreForCurrentServiceAsync(
            (coordinator, _, _, cancellationToken) => coordinator.RestoreAsync(
                commit.Sha,
                cancellationToken));
    }

    internal Task<bool> RestoreToNewBranchAsync(CommitInfo commit)
    {
        return RunRestoreForCurrentServiceAsync(async (
            coordinator,
            service,
            revision,
            cancellationToken) =>
        {
            string? branchName = await RequestBranchNameAsync(commit);
            if (!IsCurrentService(service, revision, cancellationToken)
                || string.IsNullOrWhiteSpace(branchName))
            {
                return false;
            }

            return await coordinator.RestoreToNewBranchAsync(
                commit.Sha,
                branchName.Trim(),
                cancellationToken);
        });
    }

    private Task<bool> RunRestoreForCurrentServiceAsync(
        Func<
            IProjectVersionControlCoordinator,
            IProjectVersionControlService,
            int,
            CancellationToken,
            Task<bool>> operation)
    {
        if (!TryCaptureServiceContext(
                out IProjectVersionControlCoordinator? coordinator,
                out IProjectVersionControlService? service,
                out int revision,
                out CancellationToken cancellationToken))
        {
            return Task.FromResult(false);
        }

        return RunRestoreRequestAsync(async () =>
        {
            if (!IsCurrentService(service, revision, cancellationToken))
            {
                return false;
            }

            try
            {
                bool result = await operation(
                    coordinator,
                    service,
                    revision,
                    cancellationToken);
                return IsCurrentService(service, revision, cancellationToken)
                       && result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        });
    }

    internal async Task RemoveStaleLockAsync()
    {
        if (_lockRecoveryService is null)
        {
            return;
        }

        RepositoryLockInfo? expectedLock = _lockRecoveryService.RecoverableLock;
        if (expectedLock is null)
        {
            return;
        }

        bool removed = await _lockRecoveryService.RemoveRecoverableLockAsync(
            expectedLock,
            CancellationToken.None);
        RepositoryLockInfo? remainingLock = _lockRecoveryService.RecoverableLock;
        CanRemoveStaleLock.Value = remainingLock is { RequiresManualRemoval: false };
        HasRecoverableLock.Value = remainingLock is not null;
        if (!removed && remainingLock is not null)
        {
            StaleLockGuidance.Value = FormatStaleLockManualRemovalGuidance(remainingLock);
        }
    }

    internal Task RunCommandAsync(Func<Task> operation, string commandName)
    {
        return VersionControlCommandBoundary.RunAsync(operation, _logger, commandName);
    }

    private async Task<bool> RunRestoreRequestAsync(Func<Task<bool>> operation)
    {
        if (Interlocked.CompareExchange(ref _restoreRequestActive, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            return await operation();
        }
        finally
        {
            Volatile.Write(ref _restoreRequestActive, 0);
        }
    }

    private void UpdatePrimaryAction()
    {
        VersionControlPrimaryAction action = IsRemoteOperationRunning.Value
            ? new(
                VersionControlPrimaryActionKind.Cancel,
                Strings.Cancel,
                CancelRemoteOperationCommand)
            : _hasUncommittedChanges
                ? new(
                    VersionControlPrimaryActionKind.Commit,
                    Strings.VersionControl_CommitNow,
                    CommitCommand)
                : _behindCount > 0
                    ? new(
                        VersionControlPrimaryActionKind.PullFromRemote,
                        string.Format(
                            CultureInfo.CurrentCulture,
                            Strings.VersionControl_PullCountFormat,
                            _behindCount),
                        PullCommand)
                    : _aheadCount > 0
                        ? new(
                            VersionControlPrimaryActionKind.Push,
                            string.Format(
                                CultureInfo.CurrentCulture,
                                Strings.VersionControl_PushCountFormat,
                                _aheadCount),
                            PushCommand)
                        : HasRemote.Value
                            ? new(
                                VersionControlPrimaryActionKind.UpToDate,
                                Strings.VersionControl_UpToDate,
                                _disabledPrimaryActionCommand)
                            : new(
                                VersionControlPrimaryActionKind.PublishBranch,
                                Strings.VersionControl_PublishBranch,
                                PublishBranchCommand);
        ObservePrimaryAction(action);
    }

    private void ObservePrimaryAction(VersionControlPrimaryAction action)
    {
        if (_observedPrimaryActionCommand is not null)
        {
            _observedPrimaryActionCommand.CanExecuteChanged -=
                OnPrimaryActionCanExecuteChanged;
        }

        _primaryAction.Value = action;
        _observedPrimaryActionCommand = action.Command;
        _observedPrimaryActionCommand.CanExecuteChanged +=
            OnPrimaryActionCanExecuteChanged;
        UpdatePrimaryActionCanExecute();
    }

    private void OnPrimaryActionCanExecuteChanged(object? sender, EventArgs e)
    {
        UpdatePrimaryActionCanExecute();
    }

    private void UpdatePrimaryActionCanExecute()
    {
        _isPrimaryActionEnabled.Value =
            PrimaryAction.Value.Command.CanExecute(null);
    }

    private void InvokePrimaryAction()
    {
        VersionControlPrimaryAction action = PrimaryAction.Value;
        if (action.Command.CanExecute(null))
        {
            action.Command.Execute(null);
        }
    }
}
