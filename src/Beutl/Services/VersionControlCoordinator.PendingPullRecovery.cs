using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private async Task<ProjectRecoveryResult> RunPendingPullRecoveryCycleAsync(
        string recoveryId,
        bool requireConfirmation,
        CancellationToken cancellationToken,
        PendingPullRecovery? confirmedRecovery = null,
        ProjectService.ProjectOpenAttempt? preservedOpenAttempt = null)
    {
        CancellationTokenSource? confirmationCancellation = null;
        CancellationToken lookupCancellation = default;
        try
        {
            if (requireConfirmation)
            {
                confirmationCancellation =
                    CreateProjectServiceEpochCancellation(cancellationToken);
                using (NonTransactionalOperationLease operation =
                       await BeginNonTransactionalOperationAsync(
                           confirmationCancellation.Token))
                {
                    lookupCancellation = operation.CancellationToken;
                    Project project = GetOpenProject();
                    string projectFile = GetProjectFile(project);
                    IProjectVersionControlBackend service = GetTrackedBackend();
                    confirmedRecovery = await service.ExecuteExclusiveAsync(
                        async transaction =>
                            (await transaction.GetPendingPullRecoveriesAsync(
                                operation.CancellationToken))
                            .SingleOrDefault(candidate => string.Equals(
                                candidate.Id,
                                recoveryId,
                                StringComparison.Ordinal)),
                        operation.CancellationToken);
                    if (confirmedRecovery is null
                        || !RecoveryProjectPathsEqual(
                            projectFile,
                            confirmedRecovery.ProjectFile))
                    {
                        return new ProjectRecoveryResult.NotFoundOrChanged();
                    }
                }

                if (!await ConfirmPendingPullRecoveryAsync(
                        ToRecoveryInfo(confirmedRecovery),
                        confirmationCancellation.Token)
                    .ConfigureAwait(false))
                {
                    return new ProjectRecoveryResult.Declined();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
            when (confirmationCancellation?.IsCancellationRequested == true
                  || lookupCancellation.IsCancellationRequested
                  || _lifetimeCancellation.IsCancellationRequested)
        {
            return new ProjectRecoveryResult.Unavailable();
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogInformation(
                ex,
                "Skipped pending pull recovery because its backend retired before confirmation.");
            return new ProjectRecoveryResult.Unavailable();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogInformation(
                ex,
                "Skipped pending pull recovery because it became unavailable before confirmation.");
            return new ProjectRecoveryResult.Unavailable();
        }
        finally
        {
            confirmationCancellation?.Dispose();
        }

        try
        {
            return await RunPendingPullRecoveryMutationCycleAsync(
                    recoveryId,
                    confirmedRecovery,
                    cancellationToken,
                    preservedOpenAttempt)
                .ConfigureAwait(false);
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogInformation(
                ex,
                "Skipped pending pull recovery because its backend retired after confirmation.");
            return new ProjectRecoveryResult.Unavailable();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogInformation(
                ex,
                "Skipped pending pull recovery because the project lifecycle changed after confirmation.");
            return new ProjectRecoveryResult.Unavailable();
        }
    }

    private async Task<ProjectRecoveryResult> RunPendingPullRecoveryMutationCycleAsync(
        string recoveryId,
        PendingPullRecovery? confirmedRecovery,
        CancellationToken cancellationToken,
        ProjectService.ProjectOpenAttempt? preservedOpenAttempt)
    {
        await BeginLifecycleOperationAsync(cancellationToken);
        bool gateEntered = false;
        try
        {
            await _lifecycleGate.WaitAsync(cancellationToken);
            gateEntered = true;
            ThrowIfLifecycleOperationUnavailable();
            Project project = GetOpenProject();
            IProjectVersionControlBackend ownedService = GetTrackedBackend();
            PendingPullRecovery? offeredRecovery = await ownedService.ExecuteExclusiveAsync(
                async service => (await service.GetPendingPullRecoveriesAsync(cancellationToken))
                    .SingleOrDefault(candidate => string.Equals(
                        candidate.Id,
                        recoveryId,
                        StringComparison.Ordinal)),
                cancellationToken);
            if (offeredRecovery is null)
            {
                return new ProjectRecoveryResult.NotFoundOrChanged();
            }

            string openProjectFile = GetProjectFile(project);
            if (!RecoveryProjectPathsEqual(
                    openProjectFile,
                    offeredRecovery.ProjectFile)
                || confirmedRecovery is not null
                && !PendingPullRecoveriesMatch(confirmedRecovery, offeredRecovery))
            {
                return new ProjectRecoveryResult.NotFoundOrChanged();
            }

            cancellationToken.ThrowIfCancellationRequested();
            await using ProjectService.ProjectTransitionScope transition =
                await _projectService.BeginVersionControlTransitionAsync(
                    this,
                    cancellationToken,
                    preservedOpenAttempt);
            ThrowIfLifecycleOperationUnavailable();
            if (!ReferenceEquals(_projectService.CurrentProject.Value, project)
                || !ReferenceEquals(GetOwnedBackend(), ownedService))
            {
                return new ProjectRecoveryResult.Unavailable();
            }

            using IDisposable? worktreeMutation = TryBeginWorktreeMutation();
            if (worktreeMutation is null)
            {
                return new ProjectRecoveryResult.Unavailable();
            }

            try
            {
                return await ownedService.ExecuteExclusiveAsync(
                    async service =>
                    {
                        PendingPullRecovery? recovery =
                            (await service.GetPendingPullRecoveriesAsync(cancellationToken))
                            .SingleOrDefault(candidate => string.Equals(
                                candidate.Id,
                                recoveryId,
                                StringComparison.Ordinal));
                        if (recovery is null
                            || !PendingPullRecoveriesMatch(offeredRecovery, recovery)
                            || !RecoveryProjectPathsEqual(
                                openProjectFile,
                                recovery.ProjectFile))
                        {
                            return new ProjectRecoveryResult.NotFoundOrChanged();
                        }

                        try
                        {
                            await CloseProjectForOperationAsync(
                                transition,
                                CancellationToken.None);
                            PendingPullRecoveryOutcome outcome =
                                await service.RecoverPendingPullRecoveryAsync(
                                recovery,
                                CancellationToken.None);
                            string recoveryBranchName = await FindRecoveryBranchNameAsync(
                                service,
                                recovery,
                                outcome);
                            await ReopenProjectAsync(transition, openProjectFile);
                            await service.CompletePendingPullRecoveryAsync(
                                recovery,
                                CancellationToken.None);
                            CompletePendingPullRecoveryPublication(recovery.Id);
                            PublishRecoveryOutcomeNotification(outcome, recoveryBranchName);
                            return ToProjectRecoveryResult(outcome, recoveryBranchName);
                        }
                        catch (PendingPullRecoveryPreservedException ex)
                        {
                            PublishPreservedRecoveryBranchNotification(ex.RecoveryReference);
                            return new ProjectRecoveryResult.FailedPreserved(
                                ex.RecoveryReference);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "Failed to recover pending pull state {RecoveryId}; its retained-reference state could not be verified.",
                                recovery.Id);
                            PublishNotification(() =>
                                NotificationService.ShowError(
                                    Strings.VersionControl_ErrorTitle,
                                    string.Format(
                                        Strings.VersionControl_RecoveryFailed,
                                        Strings.VersionControl_PullTransitionUncertain,
                                        GetErrorText(ex))));
                            return new ProjectRecoveryResult.FailedUncertain();
                        }
                    },
                    cancellationToken);
            }
            finally
            {
                FinishInternalTransition();
            }
        }
        finally
        {
            FinishLifecycleOperation(gateEntered);
        }
    }

    private static ProjectRecoveryInfo ToRecoveryInfo(PendingPullRecovery recovery)
    {
        return new ProjectRecoveryInfo(
            recovery.Id,
            Path.GetFileName(recovery.ProjectFile),
            recovery.CreatedAt);
    }

    private static ProjectRecoveryResult ToProjectRecoveryResult(
        PendingPullRecoveryOutcome outcome,
        string recoveryBranchName)
    {
        return outcome switch
        {
            PendingPullRecoveryOutcome.RestoredOriginal
                => new ProjectRecoveryResult.RestoredOriginal(),
            PendingPullRecoveryOutcome.ReappliedCheckpoint
                => new ProjectRecoveryResult.ReappliedCheckpoint(recoveryBranchName),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };
    }

    private void PublishRecoveryOutcomeNotification(
        PendingPullRecoveryOutcome outcome,
        string recoveryBranchName)
    {
        PublishNotification(() => NotificationService.ShowInformation(
            Strings.VersionControl,
            outcome == PendingPullRecoveryOutcome.ReappliedCheckpoint
                ? string.Format(
                    Strings.VersionControl_CheckpointReappliedOnRecoveryBranch,
                    recoveryBranchName)
                : Strings.VersionControl_PullRecovered));
    }

    // The service keeps a reapplied checkpoint on the first recovery branch name Git could create, which is
    // not the usual one when a branch such as beutl already takes its path.
    private async Task<string> FindRecoveryBranchNameAsync(
        IProjectVersionControlTransaction service,
        PendingPullRecovery recovery,
        PendingPullRecoveryOutcome outcome)
    {
        if (outcome != PendingPullRecoveryOutcome.ReappliedCheckpoint)
        {
            return recovery.RecoveryBranchName;
        }

        try
        {
            IReadOnlyList<BranchInfo> branches = await service.GetBranchesAsync(CancellationToken.None);
            return recovery.RecoveryBranchNameCandidates.FirstOrDefault(candidate =>
                       branches.Any(branch =>
                           !branch.IsRemote
                           && string.Equals(branch.Name, candidate, StringComparison.Ordinal)))
                   ?? recovery.RecoveryBranchName;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The recovery already succeeded; a wrong branch name in the message must not undo that.
            _logger.LogWarning(ex, "Failed to look up the recovery branch for {RecoveryId}.", recovery.Id);
            return recovery.RecoveryBranchName;
        }
    }

    private void PublishPreservedRecoveryBranchNotification(string recoveryReference)
    {
        PublishNotification(() => NotificationService.ShowWarning(
            Strings.VersionControl,
            string.Format(
                Strings.VersionControl_CheckpointPreservedOnRecoveryBranch,
                recoveryReference)));
    }

    private static bool PendingPullRecoveriesMatch(
        PendingPullRecovery expected,
        PendingPullRecovery actual,
        RepositoryInfo? repository = null)
    {
        return string.Equals(expected.Id, actual.Id, StringComparison.Ordinal)
               && string.Equals(
                   expected.DescriptorRef,
                   actual.DescriptorRef,
                   StringComparison.Ordinal)
               && string.Equals(
                   expected.DescriptorObject,
                   actual.DescriptorObject,
                   StringComparison.OrdinalIgnoreCase)
               && (repository is null
                   ? RecoveryProjectPathsEqual(
                       expected.ProjectFile,
                       actual.ProjectFile)
                   : RecoveryProjectPathsEqual(
                       repository,
                       expected.ProjectFile,
                       actual.ProjectFile));
    }

    private void StartPendingPullRecoveryOffer(IProjectVersionControlBackend service)
    {
        var offer = new PendingRecoveryOfferContext(
            service,
            CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token));
        PendingRecoveryOfferContext? previousOffer;
        lock (_stateGate)
        {
            if (_disposed
                || !ReferenceEquals(_state.OwnedService, service)
                || !ReferenceEquals(_state.VisibleService, service))
            {
                offer.Cancellation.Dispose();
                return;
            }

            previousOffer = _pendingRecoveryOffer;
            _pendingRecoveryOffer = offer;
            _pendingRecoveryOfferUsers++;
        }

        CancelPendingPullRecoveryOffer(previousOffer);
        _ = RunPendingPullRecoveryOfferAsync(offer);
    }

    private async Task RunPendingPullRecoveryOfferAsync(
        PendingRecoveryOfferContext offer)
    {
        IProjectVersionControlBackend service = offer.Service;
        CancellationToken cancellationToken = offer.Cancellation.Token;
        try
        {
            IReadOnlyList<PendingPullRecovery> recoveries;
            using (NonTransactionalOperationLease operation =
                   await BeginNonTransactionalOperationAsync(cancellationToken)
                       .ConfigureAwait(false))
            {
                if (!ReferenceEquals(GetOperationReadyBackend(), service))
                {
                    return;
                }

                recoveries = await service.ExecuteExclusiveAsync(
                        transaction => transaction.GetPendingPullRecoveriesAsync(
                            operation.CancellationToken),
                        operation.CancellationToken)
                    .ConfigureAwait(false);
            }

            var currentIds = recoveries
                .Select(static recovery => recovery.Id)
                .ToHashSet(StringComparer.Ordinal);
            PendingPullRecovery[] orderedRecoveries = recoveries
                .OrderBy(static candidate => candidate.CreatedAt)
                .ThenBy(static candidate => candidate.Id, StringComparer.Ordinal)
                .ToArray();
            PendingPullRecovery? recovery = null;
            bool offerRecovery = false;
            lock (_stateGate)
            {
                _offeredPendingRecoveryIds.RemoveWhere(id => !currentIds.Contains(id));
                if (!_disposed
                    && ReferenceEquals(_pendingRecoveryOffer, offer)
                    && ReferenceEquals(_state.OwnedService, service)
                    && ReferenceEquals(_state.VisibleService, service))
                {
                    recovery = orderedRecoveries.FirstOrDefault(candidate =>
                        !_offeredPendingRecoveryIds.Contains(candidate.Id));
                    if (recovery is not null)
                    {
                        offerRecovery = _offeredPendingRecoveryIds.Add(recovery.Id);
                    }
                }
            }

            if (!offerRecovery
                || recovery is null
                || !await ConfirmPendingPullRecoveryAsync(
                    ToRecoveryInfo(recovery),
                    cancellationToken))
            {
                return;
            }

            await RunPendingPullRecoveryCycleAsync(
                    recovery.Id,
                    requireConfirmation: false,
                    cancellationToken,
                    confirmedRecovery: recovery)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogInformation(
                ex,
                "Skipped a pending pull recovery offer because the project lifecycle changed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to offer a pending pull recovery.");
        }
        finally
        {
            TaskCompletionSource? quiesced = null;
            lock (_stateGate)
            {
                if (ReferenceEquals(_pendingRecoveryOffer, offer))
                {
                    _pendingRecoveryOffer = null;
                }

                _pendingRecoveryOfferUsers--;
                if (_pendingRecoveryOfferUsers == 0 && _disposed)
                {
                    quiesced = _pendingRecoveryOffersQuiesced;
                }
            }

            offer.Cancellation.Dispose();
            quiesced?.TrySetResult();
        }
    }

    private void CancelPendingPullRecoveryOffer()
    {
        PendingRecoveryOfferContext? offer;
        lock (_stateGate)
        {
            offer = _pendingRecoveryOffer;
            _pendingRecoveryOffer = null;
        }

        CancelPendingPullRecoveryOffer(offer);
    }

    private void CancelPendingPullRecoveryOffer(PendingRecoveryOfferContext? offer)
    {
        try
        {
            offer?.Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "A pending pull recovery offer cancellation callback failed.");
        }
    }

    private void ReconcileOfferedPendingRecoveryIds(
        IReadOnlyList<PendingPullRecovery> recoveries)
    {
        var currentIds = recoveries
            .Select(static recovery => recovery.Id)
            .ToHashSet(StringComparer.Ordinal);
        lock (_stateGate)
        {
            _offeredPendingRecoveryIds.RemoveWhere(id => !currentIds.Contains(id));
        }
    }

    private void CompletePendingPullRecoveryPublication(string recoveryId)
    {
        lock (_stateGate)
        {
            _offeredPendingRecoveryIds.Remove(recoveryId);
        }

        PublishPendingPullRecoveriesChanged();
    }

    private void PublishPendingPullRecoveriesChanged()
    {
        EventHandler? handlers = PendingPullRecoveriesChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "A pending pull recovery subscriber failed.");
            }
        }
    }

    private sealed record PendingRecoveryOfferContext(
        IProjectVersionControlBackend Service,
        CancellationTokenSource Cancellation);
}
