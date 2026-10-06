using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    public async Task<CommitResult> CommitManualAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        using NonTransactionalOperationLease operation =
            await BeginNonTransactionalOperationAsync(cancellationToken);
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
        IDisposable? editorSuspension = null;
        try
        {
            editorSuspension = await SuspendEditorsAsync(cancellationToken);
            // A manual version has to record what the user sees. Keep the editor frozen after the
            // save until Git and its message hooks publish the commit, so visible edits cannot land
            // after the captured tree. An identity prompt runs between helper calls with editors
            // enabled; the retry therefore acquires a fresh suspension and saves again.
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
        finally
        {
            await ReleaseEditorSuspensionAsync(editorSuspension);
        }
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
                cancellationToken)
            .ConfigureAwait(false);
        if (service is null)
        {
            return;
        }

        try
        {
            await service.CommitAllAsync(message, kind, cancellationToken).ConfigureAwait(false);
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
                        Strings.VersionControl_SaveSnapshotFailed));
            }
        }
    }

    private async Task<IProjectVersionControlBackend?> WaitForSaveSnapshotBackendAsync(
        Project savedProject,
        string savedProjectRoot,
        CancellationToken cancellationToken)
    {
        ActivationContext? waitedActivation = null;
        while (true)
        {
            ActivationContext? activation;
            lock (_stateGate)
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

                activation = _activation;
                if (activation is null
                    || ReferenceEquals(activation, waitedActivation)
                    || !PathsEqual(activation.ProjectRoot, savedProjectRoot))
                {
                    return null;
                }
            }

            waitedActivation = activation;
            await activation.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
