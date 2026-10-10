using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    public Task<CommitResult> CommitManualAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return RunOnUiThreadAsync(() => CommitManualCoreAsync(message, cancellationToken));
    }

    private async Task<CommitResult> CommitManualCoreAsync(
        string message,
        CancellationToken cancellationToken)
    {
        using OperationLease operation = await BeginOperationAsync(cancellationToken);
        using IDisposable? worktreeMutation = TryBeginWorktreeMutation();
        if (worktreeMutation is null)
        {
            throw new InvalidOperationException(Strings.VersionControl_WorkspaceBusy);
        }

        CancellationToken operationCancellation = operation.CancellationToken;
        IProjectVersionControlBackend service = GetTrackedBackend();
        string trimmedMessage = message.Trim();

        try
        {
            return await SaveAndCommitManualWithEditorSuspensionAsync(
                service,
                trimmedMessage,
                operationCancellation);
        }
        catch (GitIdentityRequiredException)
        {
            GitIdentity? identity = await RequestIdentityAsync(operationCancellation);
            if (identity is null)
            {
                throw;
            }

            operationCancellation.ThrowIfCancellationRequested();
            await service.SetLocalIdentityAsync(identity, operationCancellation);
            return await SaveAndCommitManualWithEditorSuspensionAsync(
                service,
                trimmedMessage,
                operationCancellation);
        }
    }

    private async Task<CommitResult> SaveAndCommitManualWithEditorSuspensionAsync(
        IProjectVersionControlBackend service,
        string message,
        CancellationToken cancellationToken)
    {
        // A manual version has to record what the user sees. Keep the editor frozen after the
        // save until Git and its message hooks publish the commit, so visible edits cannot land
        // after the captured tree. An identity prompt runs between helper calls with editors
        // enabled; the retry therefore acquires a fresh suspension and saves again.
        using IDisposable editorSuspension = _editorService.SuspendEditors();
        if (_projectService.CurrentProject.Value is { } project
            && !await TrySaveOpenProjectAsync(project, cancellationToken))
        {
            throw new InvalidOperationException(MessageStrings.OperationFailed);
        }

        return await service.CommitAllAsync(
            message,
            SnapshotKind.Manual,
            cancellationToken);
    }

    private async Task CommitSnapshotAsync(
        bool enabled,
        string message,
        SnapshotKind kind,
        IProjectFileWriteLease? completedWrite,
        CancellationToken cancellationToken)
    {
        if (!enabled)
        {
            return;
        }

        using IDisposable? snapshotMutation = TryBeginWorktreeMutation(completedWrite);
        if (snapshotMutation is null)
        {
            _logger.LogInformation(
                "Skipped the {SnapshotKind} project snapshot because the workspace is reserved.",
                kind);
            return;
        }

        Project? savedProject = _projectService.CurrentProject.Value;
        if (savedProject is null)
        {
            return;
        }

        string savedProjectRoot = GetProjectRoot(savedProject);
        IProjectVersionControlBackend? service = await WaitForSaveSnapshotBackendAsync(
            savedProject,
            savedProjectRoot,
            cancellationToken);
        if (service is null)
        {
            return;
        }

        try
        {
            await service.CommitAllAsync(message, kind, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create the {SnapshotKind} project snapshot.", kind);
            if (kind == SnapshotKind.Save)
            {
                PublishNotification(() =>
                    NotificationService.ShowWarning(
                        Strings.VersionControl,
                        FormatSnapshotFailure(ex)));
            }
        }
    }

    // Names what stopped the snapshot, such as the output of a pre-commit hook that rejected it, so
    // the user knows what to fix before saving again.
    private static string FormatSnapshotFailure(Exception exception)
    {
        return string.Format(
            Strings.VersionControl_SaveSnapshotFailedFormat,
            GetErrorText(exception));
    }

    private async Task<IProjectVersionControlBackend?> WaitForSaveSnapshotBackendAsync(
        Project savedProject,
        string savedProjectRoot,
        CancellationToken cancellationToken)
    {
        ActivationContext? waitedActivation = null;
        while (true)
        {
            if (_disposed
                || !ReferenceEquals(_projectService.CurrentProject.Value, savedProject)
                || _state.ProjectRoot is not { } currentRoot
                || !PathsEqual(currentRoot, savedProjectRoot))
            {
                return null;
            }

            if (ReferenceEquals(_state.OwnedService, _state.VisibleService)
                && _state.OwnedService is { Repository: not null } ready)
            {
                return ready;
            }

            ActivationContext? activation = _activation;
            if (activation is null
                || ReferenceEquals(activation, waitedActivation)
                || !PathsEqual(activation.ProjectRoot, savedProjectRoot))
            {
                return null;
            }

            waitedActivation = activation;
            await activation.Completion.WaitAsync(cancellationToken);
        }
    }
}
