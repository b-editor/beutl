using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private async Task PrepareProjectClosingAsync(
        ProjectService.ProjectCloseContext closeContext,
        CancellationToken cancellationToken)
    {
        if (IsInternalVersionControlTransition())
        {
            AdvanceProjectServiceEpoch();
            return;
        }

        // The close can wait on Git for a while, first for work already running and then for the close
        // snapshot, and the editor cannot be used meanwhile. The editor area shows the close instead of an
        // editor that looks usable, until the close completes or is aborted.
        IDisposable? closingPresentation = null;
        bool completionRegistered = false;
        try
        {
            NonTransactionalCloseBarrier? closeBarrier =
                await TryBeginNonTransactionalCloseBarrierAsync(
                        () => closingPresentation = _editorService.BeginLifecycleActivity(
                            ProjectLifecycleActivity.ClosingProject),
                        cancellationToken)
                    .ConfigureAwait(false);
            if (closeBarrier is null)
            {
                return;
            }

            // An inline decision must not keep the project close waiting for activation.
            PendingRepositoryAdoption?.Respond(false);
            AdvanceProjectServiceEpoch();

            IDisposable? editorSuspension = null;
            try
            {
                if (closeContext.CloseIntent == ProjectService.ProjectCloseIntent.SaveChanges
                    && _config.AutoCommitOnClose
                    && GetOwnedBackend()?.Repository is not null
                    && _projectService.CurrentProject.Value is not null)
                {
                    editorSuspension = await SuspendEditorsAsync(cancellationToken);
                }

                lock (_stateGate)
                {
                    _preparedCloseBarriers.Add(closeContext, closeBarrier);
                }

                closeContext.RegisterCompletion(
                    projectClosed => CompletePreparedCloseAsync(
                        closeContext,
                        closeBarrier,
                        editorSuspension,
                        closingPresentation,
                        projectClosed));
                completionRegistered = true;
            }
            finally
            {
                if (!completionRegistered)
                {
                    lock (_stateGate)
                    {
                        _preparedCloseBarriers.Remove(closeContext);
                    }

                    try
                    {
                        await ReleaseEditorSuspensionAsync(editorSuspension);
                    }
                    finally
                    {
                        await closeBarrier.CompleteAsync(projectClosed: false).ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            if (!completionRegistered)
            {
                closingPresentation?.Dispose();
            }
        }

        if (closeContext.CloseIntent == ProjectService.ProjectCloseIntent.SaveChanges)
        {
            await TrySaveForCloseSnapshotAsync(closeContext, cancellationToken).ConfigureAwait(false);
        }
    }

    // The close snapshot runs from ClosingFinalizing, by which point the editor host has disposed
    // every tab, so in-memory edits can only reach disk from this earlier ClosingPreparing phase.
    private async Task TrySaveForCloseSnapshotAsync(
        ProjectService.ProjectCloseContext closeContext,
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
            await _editorService.BeginProjectFileWriteAsync(cancellationToken).ConfigureAwait(false);
        if (!await TrySaveOpenProjectAsync(project, cancellationToken).ConfigureAwait(false))
        {
            if (await ConfirmCloseWithoutSnapshotAsync(cancellationToken).ConfigureAwait(false))
            {
                lock (_stateGate)
                {
                    _closesWithoutSnapshot.Add(closeContext);
                }

                closeContext.PreparedEditorService = _editorService;
                return;
            }

            PublishNotification(() =>
                NotificationService.ShowWarning(
                    Strings.VersionControl,
                    MessageStrings.OperationFailed));
            throw new ProjectCloseAbortedException(MessageStrings.OperationFailed);
        }

        closeContext.PreparedEditorService = _editorService;
    }

    // Runs once the close finishes or is aborted: the editors come back before the barrier lets
    // operations start again, and the close stays on screen until both are done.
    private async Task CompletePreparedCloseAsync(
        ProjectService.ProjectCloseContext closeContext,
        NonTransactionalCloseBarrier closeBarrier,
        IDisposable? editorSuspension,
        IDisposable? closingPresentation,
        bool projectClosed)
    {
        try
        {
            await ReleaseEditorSuspensionAsync(editorSuspension);
        }
        finally
        {
            try
            {
                await CompletePreparedCloseBarrierAsync(
                    closeContext,
                    closeBarrier,
                    projectClosed);
            }
            finally
            {
                closingPresentation?.Dispose();
            }
        }
    }

    private async Task CompletePreparedCloseBarrierAsync(
        ProjectService.ProjectCloseContext closeContext,
        NonTransactionalCloseBarrier closeBarrier,
        bool projectClosed)
    {
        lock (_stateGate)
        {
            _preparedCloseBarriers.Remove(closeContext);
            _closesWithoutSnapshot.Remove(closeContext);
        }

        await closeBarrier.CompleteAsync(projectClosed).ConfigureAwait(false);
    }

    private async Task NotifyProjectClosingAsync(
        ProjectService.ProjectCloseContext closeContext,
        CancellationToken cancellationToken)
    {
        if (IsInternalVersionControlTransition())
        {
            return;
        }

        NonTransactionalCloseBarrier? closeBarrier;
        bool closeSnapshotDeclined;
        lock (_stateGate)
        {
            _preparedCloseBarriers.TryGetValue(closeContext, out closeBarrier);
            closeSnapshotDeclined = _closesWithoutSnapshot.Contains(closeContext);
        }

        if (closeBarrier is not null)
        {
            await NotifyClosingCoreAsync(
                    closeContext.CloseIntent,
                    allowCloseSnapshot: !closeSnapshotDeclined,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task NotifyClosingCoreAsync(
        ProjectService.ProjectCloseIntent closeIntent,
        bool allowCloseSnapshot,
        CancellationToken closeCancellation)
    {
        bool closeSnapshotRequested = false;
        ActivationContext? activation;
        string? projectRoot;
        long activationRevision;
        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }

            activation = _activation;
            projectRoot = _state.ProjectRoot;
            activationRevision = _latestActivationRevision;
        }

        if (activation is not null)
        {
            await activation.Completion.WaitAsync(closeCancellation).ConfigureAwait(false);
        }

        closeCancellation.ThrowIfCancellationRequested();
        try
        {
            IProjectVersionControlBackend service;
            bool finalSnapshotRequested;
            lock (_stateGate)
            {
                if (!IsClosingProjectCurrentLocked(projectRoot, activationRevision))
                {
                    return;
                }

                IProjectVersionControlBackend? ownedService = _state.OwnedService;
                if (ownedService is null)
                {
                    return;
                }

                service = ownedService;
                // A discard close saves nothing, so it must not commit what autosave or another
                // tool already wrote either. Nor may a close the user continued after its save
                // failed, since the project on disk may be half-saved.
                finalSnapshotRequested = _config.AutoCommitOnClose
                                         && closeIntent == ProjectService.ProjectCloseIntent.SaveChanges
                                         && allowCloseSnapshot;
            }

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

            ProjectVersionControlFinalSnapshot? finalSnapshot;
            bool schedulePublication;
            lock (_stateGate)
            {
                if (!IsClosingProjectCurrentLocked(projectRoot, activationRevision)
                    || !ReferenceEquals(_state.OwnedService, service))
                {
                    return;
                }

                finalSnapshot =
                    finalSnapshotRequested
                    && snapshotReserved
                        ? new ProjectVersionControlFinalSnapshot(
                            CloseSnapshotMessage,
                            SnapshotKind.Close)
                        : null;

                bool visibilityHidden = ReferenceEquals(_state.VisibleService, service);
                schedulePublication = visibilityHidden
                    && TransitionStateLocked(
                        _state with
                        {
                            VisibleService = null,
                            IsTracked = false,
                        });
            }

            SchedulePublicationDrain(schedulePublication);
            await FlushPublicationDrainAsync().ConfigureAwait(false);
            closeCancellation.ThrowIfCancellationRequested();

            try
            {
                CommitResult? result = await service.RetireAsync(finalSnapshot).ConfigureAwait(false);
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
                        Strings.VersionControl_SaveSnapshotFailed));
            }
        }
    }

    // Whether closing the open project waits on version control: an operation still running, an
    // activation still deciding how the project is tracked, or a tracked backend to retire.
    private bool HasVersionControlWorkToFinishLocked()
    {
        return !_disposed
               && _projectService.CurrentProject.Value is not null
               && (_operationUsers > 0
                   || _activation is not null
                   || _state.OwnedService?.Repository is not null);
    }

    // The close still concerns the project and the activation it captured before it waited.
    private bool IsClosingProjectCurrentLocked(string? projectRoot, long activationRevision)
    {
        return !_disposed
               && projectRoot is not null
               && activationRevision == _latestActivationRevision
               && _state.ProjectRoot is { } currentRoot
               && PathsEqual(currentRoot, projectRoot);
    }

    // versionControlWorkPending runs once the barrier stops new operations, and only when the close then
    // waits on version control. Deciding under the same lock means no operation can start unnoticed
    // between the decision and the wait it causes.
    private async Task<NonTransactionalCloseBarrier?>
        TryBeginNonTransactionalCloseBarrierAsync(
            Action versionControlWorkPending,
            CancellationToken cancellationToken)
    {
        lock (_stateGate)
        {
            if (_disposed)
            {
                return null;
            }

            _closeBarrierUsers++;
        }

        CancellationTokenSource? closeCancellation = null;
        CancellationTokenSource? operationEpochCancellation = null;
        bool gateEntered = false;
        bool barrierEntered = false;
        try
        {
            closeCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCancellation.Token);
            await _operationCloseGate.WaitAsync(closeCancellation.Token).ConfigureAwait(false);
            gateEntered = true;

            Task operationsQuiesced;
            bool disposed;
            bool workPending = false;
            lock (_stateGate)
            {
                disposed = _disposed;
                if (!disposed)
                {
                    _operationCloseBarrierActive = true;
                    operationEpochCancellation = _operationEpochCancellation
                        ?? new CancellationTokenSource();
                    _operationEpochCancellation = operationEpochCancellation;
                    operationsQuiesced = _operationUsers == 0
                        ? Task.CompletedTask
                        : (_operationsQuiesced ??= CreateCompletionSource()).Task;
                    workPending = HasVersionControlWorkToFinishLocked();
                    barrierEntered = true;
                }
                else
                {
                    operationsQuiesced = Task.CompletedTask;
                }
            }

            if (disposed)
            {
                closeCancellation.Dispose();
                FinishNonTransactionalCloseBarrierWaiter(gateEntered);
                return null;
            }

            if (workPending)
            {
                versionControlWorkPending();
            }

            Exception? cancellationFailure = null;
            try
            {
                operationEpochCancellation!.Cancel();
            }
            catch (Exception ex)
            {
                cancellationFailure = ex;
            }

            await operationsQuiesced.ConfigureAwait(false);
            if (cancellationFailure is not null)
            {
                _logger.LogError(
                    cancellationFailure,
                    "An operation cancellation callback failed while closing the project.");
            }

            closeCancellation.Token.ThrowIfCancellationRequested();
            return new NonTransactionalCloseBarrier(
                this,
                closeCancellation,
                operationEpochCancellation!);
        }
        catch
        {
            closeCancellation?.Dispose();
            if (barrierEntered)
            {
                FinishNonTransactionalCloseBarrier(
                    operationEpochCancellation!);
                TryStartPendingConfigurationActivation();
            }
            else
            {
                FinishNonTransactionalCloseBarrierWaiter(gateEntered);
            }

            throw;
        }
    }

    private void FinishNonTransactionalCloseBarrier(
        CancellationTokenSource operationEpochCancellation)
    {
        TaskCompletionSource? quiesced = null;
        bool clearProjectState = false;
        lock (_stateGate)
        {
            if (ReferenceEquals(_operationEpochCancellation, operationEpochCancellation))
            {
                _operationEpochCancellation = _disposed
                    ? null
                    : new CancellationTokenSource();
            }

            _operationCloseBarrierActive = false;
            _closeBarrierUsers--;
            if (_closeBarrierUsers == 0)
            {
                quiesced = _closeBarriersQuiesced;
                _closeBarriersQuiesced = null;
                clearProjectState = _disposed
                                    && _lifecycleUsers == 0
                                    && _operationUsers == 0;
            }
        }

        try
        {
            operationEpochCancellation.Dispose();
        }
        finally
        {
            _operationCloseGate.Release();
            try
            {
                if (clearProjectState)
                {
                    ClearProjectState();
                }
            }
            finally
            {
                quiesced?.TrySetResult();
            }
        }
    }

    private Task CompleteNonTransactionalCloseBarrierAsync(
        CancellationTokenSource operationEpochCancellation,
        bool projectClosed)
    {
        if (projectClosed)
        {
            lock (_stateGate)
            {
                _pendingConfigurationActivation = null;
            }
        }

        FinishNonTransactionalCloseBarrier(operationEpochCancellation);
        TryStartPendingConfigurationActivation();
        return Task.CompletedTask;
    }

    private void FinishNonTransactionalCloseBarrierWaiter(bool gateEntered)
    {
        TaskCompletionSource? quiesced = null;
        bool clearProjectState = false;
        lock (_stateGate)
        {
            _closeBarrierUsers--;
            if (_closeBarrierUsers == 0)
            {
                quiesced = _closeBarriersQuiesced;
                _closeBarriersQuiesced = null;
                clearProjectState = _disposed
                                    && _lifecycleUsers == 0
                                    && _operationUsers == 0;
            }
        }

        if (gateEntered)
        {
            _operationCloseGate.Release();
        }

        try
        {
            if (clearProjectState)
            {
                ClearProjectState();
            }
        }
        finally
        {
            quiesced?.TrySetResult();
            TryStartPendingConfigurationActivation();
        }
    }

    private sealed class NonTransactionalCloseBarrier
    {
        private VersionControlCoordinator? _owner;
        private readonly CancellationTokenSource _cancellation;
        private readonly CancellationTokenSource _operationEpochCancellation;

        public NonTransactionalCloseBarrier(
            VersionControlCoordinator owner,
            CancellationTokenSource cancellation,
            CancellationTokenSource operationEpochCancellation)
        {
            _owner = owner;
            _cancellation = cancellation;
            _operationEpochCancellation = operationEpochCancellation;
        }

        public async Task CompleteAsync(bool projectClosed)
        {
            VersionControlCoordinator? owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null)
            {
                return;
            }

            try
            {
                _cancellation.Dispose();
            }
            finally
            {
                await owner.CompleteNonTransactionalCloseBarrierAsync(
                        _operationEpochCancellation,
                        projectClosed)
                    .ConfigureAwait(false);
            }
        }
    }
}
