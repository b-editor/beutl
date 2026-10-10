using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private Task PrepareProjectClosingAsync(
        ProjectService.ProjectCloseContext closeContext,
        CancellationToken cancellationToken)
    {
        return RunOnUiThreadAsync(() => PrepareProjectClosingCoreAsync(closeContext, cancellationToken));
    }

    // From here until the close completes or is aborted, no operation can start: the one running is
    // canceled, and the close holds the operation gate once that operation has let go of it.
    private async Task PrepareProjectClosingCoreAsync(
        ProjectService.ProjectCloseContext closeContext,
        CancellationToken cancellationToken)
    {
        if (IsInternalVersionControlTransition())
        {
            AdvanceProjectServiceEpoch();
            return;
        }

        if (_disposed || _close is not null)
        {
            return;
        }

        var close = new PreparedClose(closeContext, BeginWork());
        _close = close;
        bool completionRegistered = false;
        try
        {
            // The close can wait on Git for a while, first for work already running and then for the
            // close snapshot, and the editor cannot be used meanwhile. The editor area shows the close
            // instead of an editor that looks usable, until the close completes or is aborted.
            if (HasVersionControlWorkToFinish())
            {
                close.Presentation = _editorService.BeginLifecycleActivity(
                    ProjectLifecycleActivity.ClosingProject);
            }

            try
            {
                _operationEpochCancellation.Cancel();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "An operation cancellation callback failed while closing the project.");
            }

            AdvanceProjectServiceEpoch();
            // An inline decision must not keep the project close waiting for activation.
            PendingRepositoryAdoption?.Respond(false);
            using (var gateCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken,
                       _lifetimeCancellation.Token))
            {
                await _operationGate.WaitAsync(gateCancellation.Token);
            }

            close.HoldsOperationGate = true;
            if (closeContext.CloseIntent == ProjectService.ProjectCloseIntent.SaveChanges
                && _config.AutoCommitOnClose
                && GetOwnedBackend()?.Repository is not null
                && _projectService.CurrentProject.Value is not null)
            {
                close.EditorSuspension = _editorService.SuspendEditors();
            }

            closeContext.RegisterCompletion(projectClosed => RunOnUiThreadAsync(() =>
            {
                CompletePreparedClose(close, projectClosed);
                return Task.CompletedTask;
            }));
            completionRegistered = true;
        }
        finally
        {
            if (!completionRegistered)
            {
                CompletePreparedClose(close, projectClosed: false);
            }
        }

        if (closeContext.CloseIntent == ProjectService.ProjectCloseIntent.SaveChanges)
        {
            await TrySaveForCloseSnapshotAsync(close, cancellationToken);
        }
    }

    // The close snapshot runs from ClosingFinalizing, by which point the editor host has disposed
    // every tab, so in-memory edits can only reach disk from this earlier ClosingPreparing phase.
    private async Task TrySaveForCloseSnapshotAsync(
        PreparedClose close,
        CancellationToken cancellationToken)
    {
        if (!_config.AutoCommitOnClose
            || GetOwnedBackend()?.Repository is null
            || _projectService.CurrentProject.Value is not { } project)
        {
            return;
        }

        // Continuing disposes the tabs, so only the user can accept losing the edits that did not save.
        // That close records no snapshot, because the half-saved project must not become the version to
        // come back to. Declining aborts the close and keeps the edits.
        using IProjectFileWriteLease closeWrite =
            await _editorService.BeginProjectFileWriteAsync(cancellationToken);
        if (!await TrySaveOpenProjectAsync(project, cancellationToken))
        {
            if (await ConfirmCloseWithoutSnapshotAsync(cancellationToken))
            {
                close.SnapshotDeclined = true;
                close.Context.PreparedEditorService = _editorService;
                return;
            }

            PublishNotification(() =>
                NotificationService.ShowWarning(
                    Strings.VersionControl,
                    MessageStrings.OperationFailed));
            throw new ProjectCloseAbortedException(MessageStrings.OperationFailed);
        }

        close.Context.PreparedEditorService = _editorService;
    }

    // Runs once the close finishes or is aborted: the editors come back before operations can start
    // again, and the close stays on screen until both are done.
    private void CompletePreparedClose(PreparedClose close, bool projectClosed)
    {
        try
        {
            close.EditorSuspension?.Dispose();
        }
        finally
        {
            if (ReferenceEquals(_close, close))
            {
                _close = null;
            }

            if (projectClosed)
            {
                _pendingConfigurationActivation = null;
            }

            if (!_disposed && _operationEpochCancellation.IsCancellationRequested)
            {
                _operationEpochCancellation.Dispose();
                _operationEpochCancellation = new CancellationTokenSource();
            }

            if (close.HoldsOperationGate)
            {
                close.HoldsOperationGate = false;
                _operationGate.Release();
            }

            close.Presentation?.Dispose();
            close.Work.Dispose();
            StartConfigurationActivation();
        }
    }

    private Task NotifyProjectClosingAsync(
        ProjectService.ProjectCloseContext closeContext,
        CancellationToken cancellationToken)
    {
        return RunOnUiThreadAsync(async () =>
        {
            if (IsInternalVersionControlTransition()
                || _close is not { } close
                || !ReferenceEquals(close.Context, closeContext))
            {
                return;
            }

            await NotifyClosingCoreAsync(
                closeContext.CloseIntent,
                allowCloseSnapshot: !close.SnapshotDeclined,
                cancellationToken);
        });
    }

    private async Task NotifyClosingCoreAsync(
        ProjectService.ProjectCloseIntent closeIntent,
        bool allowCloseSnapshot,
        CancellationToken closeCancellation)
    {
        bool closeSnapshotRequested = false;
        if (_disposed)
        {
            return;
        }

        ActivationContext? activation = _activation;
        string? projectRoot = _state.ProjectRoot;
        if (activation is not null)
        {
            await activation.Completion.WaitAsync(closeCancellation);
        }

        closeCancellation.ThrowIfCancellationRequested();
        try
        {
            if (!IsClosingProjectCurrent(projectRoot, activation)
                || _state.OwnedService is not { } service)
            {
                return;
            }

            // A discard close saves nothing, so it must not commit what autosave or another
            // tool already wrote either. Nor may a close the user continued after its save
            // failed, since the project on disk may be half-saved.
            bool finalSnapshotRequested = _config.AutoCommitOnClose
                                          && closeIntent == ProjectService.ProjectCloseIntent.SaveChanges
                                          && allowCloseSnapshot;

            bool snapshotRequiresReservation =
                finalSnapshotRequested && service.Repository is not null;
            closeSnapshotRequested = snapshotRequiresReservation;
            using IDisposable? snapshotMutation = snapshotRequiresReservation
                ? TryBeginWorktreeMutation()
                : null;
            bool snapshotReserved = !snapshotRequiresReservation || snapshotMutation is not null;
            if (!snapshotReserved)
            {
                _logger.LogInformation(
                    "Skipped the {SnapshotKind} project snapshot because the workspace is reserved.",
                    SnapshotKind.Close);
            }

            if (!IsClosingProjectCurrent(projectRoot, activation)
                || !ReferenceEquals(_state.OwnedService, service))
            {
                return;
            }

            ProjectVersionControlFinalSnapshot? finalSnapshot =
                finalSnapshotRequested
                && snapshotReserved
                    ? new ProjectVersionControlFinalSnapshot(
                        CloseSnapshotMessage,
                        SnapshotKind.Close)
                    : null;
            if (ReferenceEquals(_state.VisibleService, service))
            {
                SetState(_state with
                {
                    VisibleService = null,
                    IsTracked = false,
                });
            }

            closeCancellation.ThrowIfCancellationRequested();

            try
            {
                CommitResult? result = await service.RetireAsync(finalSnapshot);
                if (result is CommitResult.SkippedNoIdentity)
                {
                    PublishNotification(() =>
                        NotificationService.ShowWarning(
                            Strings.VersionControl,
                            Strings.VersionControl_MissingIdentityNotice));
                }
            }
            finally
            {
                DetachRetiredService(service);
            }
        }
        catch (OperationCanceledException) when (closeCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retire version control while closing the project.");
            if (closeSnapshotRequested)
            {
                PublishNotification(() =>
                    NotificationService.ShowWarning(
                        Strings.VersionControl,
                        FormatSnapshotFailure(ex)));
            }
        }
    }

    // Whether closing the open project waits on version control: an operation still running, an
    // activation still deciding how the project is tracked, or a tracked backend to retire.
    private bool HasVersionControlWorkToFinish()
    {
        return !_disposed
               && _projectService.CurrentProject.Value is not null
               && (_operationGate.CurrentCount == 0
                   || _activation is not null
                   || _state.OwnedService?.Repository is not null);
    }

    // The close still concerns the project it captured before it waited, and no other activation has
    // started deciding how that project is tracked.
    private bool IsClosingProjectCurrent(string? projectRoot, ActivationContext? awaitedActivation)
    {
        return !_disposed
               && projectRoot is not null
               && (_activation is null || ReferenceEquals(_activation, awaitedActivation))
               && _state.ProjectRoot is { } currentRoot
               && PathsEqual(currentRoot, projectRoot);
    }

    // A close of the open project that version control takes part in, from ClosingPreparing until the
    // close completes or is aborted.
    private sealed class PreparedClose(
        ProjectService.ProjectCloseContext context,
        IDisposable work)
    {
        public ProjectService.ProjectCloseContext Context { get; } = context;

        // Counts the close as running work, which disposal waits for.
        public IDisposable Work { get; } = work;

        public bool HoldsOperationGate { get; set; }

        // Set when the user continued a close whose save failed, which then records no snapshot.
        public bool SnapshotDeclined { get; set; }

        public IDisposable? EditorSuspension { get; set; }

        public IDisposable? Presentation { get; set; }
    }
}
