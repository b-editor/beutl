using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    // Canonical, not merely fully qualified: RepositoryInfo decides identity on symlink- and
    // casing-resolved paths, so a marker keyed lexically is missed when the same project is reopened
    // through a different alias, and the recovery is then re-offered or the open aborted.
    internal static string GetOpeningRecoveryKey(string projectFile)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(
                VersionControlPathComparison.ResolveCanonicalPath(projectFile));
        }
        catch (IOException)
        {
            // A path that cannot be canonicalized at all - a symbolic-link cycle, or a component
            // that cannot be read - keeps its lexical key rather than failing the open. Aliases of
            // such a path no longer collapse, which is exactly the behaviour before canonical keys.
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectFile));
        }
    }

    private static void EnsurePendingRecoveryPathIsSafeForOpen(
        RepositoryInfo? repository,
        PendingPullRecovery? recovery,
        string projectFile)
    {
        if (repository is not null && recovery is not null)
        {
            EnsureProjectFileIsPhysicallyContained(repository, projectFile);
        }
    }

    private async Task<bool> TryRecoverPendingPullBeforeOpeningAsync(
        string projectFile,
        CancellationToken cancellationToken,
        string? requiredRecoveryId = null)
    {
        PendingPullRecoveryOpenSelection? selection = null;
        try
        {
            OpeningRepositoryInspection? inspection =
                await DiscoverPendingPullRecoveryForOpeningAsync(
                    projectFile,
                    cancellationToken,
                    requiredRecoveryId)
                .ConfigureAwait(false);
            selection = inspection?.Recovery;
            if (selection is null)
            {
                return false;
            }

            bool accepted = selection.AlreadyApplied
                            || await ConfirmPendingPullRecoveryAsync(
                                ToRecoveryInfo(selection.Recovery),
                                cancellationToken);
            selection = selection with { Accepted = accepted };
            if (!accepted)
            {
                IsPendingRecoveryPathSafeForOpen(selection);
                return false;
            }

            ProjectOpenPreparationResult result =
                await ApplyPendingPullRecoveryBeforeOpeningAsync(
                        selection,
                        cancellationToken)
                    .ConfigureAwait(false);
            return result == ProjectOpenPreparationResult.Proceed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to inspect pending pull recovery before opening {ProjectFile}.",
                projectFile);
            if (selection is not null)
            {
                IsPendingRecoveryPathSafeForOpen(selection);
            }

            return false;
        }
    }

    private async Task<OpeningRepositoryInspection?>
        DiscoverPendingPullRecoveryForOpeningAsync(
            string projectFile,
            CancellationToken cancellationToken,
            string? requiredRecoveryId = null)
    {
        string canonicalProjectFile = GetOpeningRecoveryKey(projectFile);
        PendingOpeningPullRecovery? cleanupCandidate;
        lock (_stateGate)
        {
            _openingPullRecoveries.TryGetValue(
                canonicalProjectFile,
                out cleanupCandidate);
        }

        bool noRecoveryToGuard = requiredRecoveryId is null && cleanupCandidate is null;
        IProjectVersionControlBackend? discoveryService = null;
        IProjectVersionControlBackend? trackedService = null;
        try
        {
            discoveryService = CreateTemporaryBackend(repository: null, projectFile);
            GitAvailability availability = await discoveryService.GetAvailabilityAsync(cancellationToken)
                .ConfigureAwait(false);
            if (availability.State != GitAvailabilityState.Installed)
            {
                return null;
            }

            string projectRoot = GetProjectDirectory(projectFile);
            RepositoryInfo? repository;
            try
            {
                repository = await discoveryService.DiscoverRepositoryAsync(
                        projectRoot,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ArgumentException ex)
                when (noRecoveryToGuard
                      && projectRoot.Any(char.IsControl))
            {
                // Git commands cannot safely carry control-character repository paths through the
                // line-oriented discovery protocol. Version control is optional for a normal open,
                // so degrade to an untracked project unless a known recovery still needs guarding.
                _logger.LogInformation(
                    ex,
                    "Opening {ProjectFile} without version control because its path is unsupported by repository discovery.",
                    projectFile);
                return null;
            }
            catch (Exception ex)
                when (ex is not OperationCanceledException and not OutOfMemoryException
                      && noRecoveryToGuard)
            {
                // Git refused the folder itself (for example dubious ownership on a shared or
                // removable volume), so no pull recovery can run there either. Open the project
                // without version control and say why instead of refusing the project.
                _logger.LogWarning(
                    ex,
                    "Opening {ProjectFile} without version control because repository discovery failed.",
                    projectFile);
                PublishNotification(() =>
                    NotificationService.ShowWarning(
                        Strings.VersionControl,
                        string.Format(
                            Strings.VersionControl_OpenedWithoutVersionControlFormat,
                            GetErrorText(ex))));
                return null;
            }

            if (repository is null)
            {
                return null;
            }

            // Activation leaves a branch with no commit yet for initialization, so asking to share
            // the enclosing repository now would lead nowhere.
            if (repository.IsNestedInForeignRepo
                && noRecoveryToGuard
                && !await discoveryService.HasCheckedOutCommitAsync(repository, cancellationToken)
                    .ConfigureAwait(false))
            {
                return null;
            }

            bool enclosingRepositoryAccepted = !repository.IsNestedInForeignRepo
                                                || await ConfirmUseEnclosingRepositoryIfNeededAsync(
                                                    discoveryService,
                                                    repository,
                                                    cancellationToken);
            if (!enclosingRepositoryAccepted)
            {
                return new OpeningRepositoryInspection(
                    repository,
                    projectFile,
                    EnclosingRepositoryAccepted: false,
                    Recovery: null);
            }

            trackedService = CreateTemporaryBackend(repository, projectFile);
            IReadOnlyList<PendingPullRecovery> recoveries =
                await trackedService.ExecuteExclusiveAsync(
                        transaction => transaction.GetPendingPullRecoveriesAsync(cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
            PendingPullRecovery? recovery = OrderOldestFirst(recoveries
                    .Where(candidate => RecoveryProjectPathsEqual(
                                            repository,
                                            candidate.ProjectFile,
                                            projectFile)
                                        && (requiredRecoveryId is null
                                            || string.Equals(
                                                candidate.Id,
                                                requiredRecoveryId,
                                                StringComparison.Ordinal))))
                .FirstOrDefault();
            if (recovery is null)
            {
                if (requiredRecoveryId is null && cleanupCandidate is not null)
                {
                    ForgetOpeningPullRecoveryMarker(canonicalProjectFile, cleanupCandidate);
                }

                return new OpeningRepositoryInspection(
                    repository,
                    projectFile,
                    EnclosingRepositoryAccepted: true,
                    Recovery: null);
            }

            PendingOpeningPullRecovery? appliedMarker = FindAppliedOpeningPullRecoveryMarker(
                canonicalProjectFile,
                repository,
                recovery);
            return new OpeningRepositoryInspection(
                repository,
                projectFile,
                EnclosingRepositoryAccepted: true,
                Recovery: new PendingPullRecoveryOpenSelection(
                    repository,
                    recovery,
                    projectFile,
                    Accepted: false,
                    AppliedMarker: appliedMarker));
        }
        finally
        {
            if (!ReferenceEquals(trackedService, discoveryService))
            {
                DisposeService(trackedService);
            }

            DisposeService(discoveryService);
        }
    }

    private async Task<ProjectOpenPreparationResult>
        ApplyPendingPullRecoveryBeforeOpeningAsync(
            PendingPullRecoveryOpenSelection selection,
            CancellationToken cancellationToken)
    {
        using NonTransactionalOperationLease? operation =
            TryBeginNonTransactionalOperation(cancellationToken);
        if (operation is null)
        {
            return ProjectOpenPreparationResult.Abort;
        }

        using IDisposable? worktreeMutation = TryBeginWorktreeMutation();
        if (worktreeMutation is null)
        {
            return ProjectOpenPreparationResult.Abort;
        }

        IProjectVersionControlBackend? discoveryService = null;
        IProjectVersionControlBackend? trackedService = null;
        try
        {
            CancellationToken operationCancellation = operation.CancellationToken;
            string canonicalProjectFile = GetOpeningRecoveryKey(selection.ProjectFile);
            discoveryService = CreateTemporaryBackend(repository: null, selection.ProjectFile);
            RepositoryInfo? repository = await DiscoverInstalledRepositoryAsync(
                    discoveryService,
                    selection.ProjectFile,
                    operationCancellation)
                .ConfigureAwait(false);
            if (repository is null || !RepositoriesEqual(repository, selection.Repository))
            {
                return ProjectOpenPreparationResult.Abort;
            }

            trackedService = CreateTemporaryBackend(repository, selection.ProjectFile);
            string recoveryBranchName = selection.Recovery.RecoveryBranchName;
            PendingPullRecoveryOutcome? outcome = await trackedService.ExecuteExclusiveAsync(
                    async transaction =>
                    {
                        PendingPullRecovery current = await GetUnchangedPendingPullRecoveryAsync(
                            transaction,
                            selection.Recovery,
                            repository,
                            selection.ProjectFile,
                            operationCancellation);
                        if (selection.AlreadyApplied
                            && !IsAppliedOpeningPullRecoveryMarkerCurrent(
                                canonicalProjectFile,
                                selection,
                                repository))
                        {
                            throw new PendingPullRecoveryChangedException(
                                selection.Recovery.DescriptorRef);
                        }

                        if (!selection.Accepted || selection.AlreadyApplied)
                        {
                            return (PendingPullRecoveryOutcome?)null;
                        }

                        PendingPullRecoveryOutcome recovered =
                            await transaction.RecoverPendingPullRecoveryAsync(
                                current,
                                CancellationToken.None);
                        recoveryBranchName = await FindRecoveryBranchNameAsync(
                            transaction,
                            current,
                            recovered);
                        return (PendingPullRecoveryOutcome?)recovered;
                    },
                    operationCancellation)
                .ConfigureAwait(false);
            if (!IsPendingRecoveryPathSafeForOpen(selection))
            {
                return ProjectOpenPreparationResult.Abort;
            }

            if (outcome is null)
            {
                return ProjectOpenPreparationResult.Proceed;
            }

            lock (_stateGate)
            {
                _openingPullRecoveries[canonicalProjectFile] =
                    new PendingOpeningPullRecovery(repository, selection.Recovery);
            }

            PublishRecoveryOutcomeNotification(outcome.Value, recoveryBranchName);
            return ProjectOpenPreparationResult.Proceed;
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
            return ProjectOpenPreparationResult.Abort;
        }
        catch (PendingPullRecoveryPreservedException ex)
        {
            PublishPreservedRecoveryBranchNotification(ex.RecoveryReference);
            IsPendingRecoveryPathSafeForOpen(selection);
            return ProjectOpenPreparationResult.Abort;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to validate or recover a pending pull before opening {ProjectFile}; its retained-reference state could not be verified.",
                selection.ProjectFile);
            IsPendingRecoveryPathSafeForOpen(selection);
            return ProjectOpenPreparationResult.Abort;
        }
        finally
        {
            if (!ReferenceEquals(trackedService, discoveryService))
            {
                DisposeService(trackedService);
            }

            DisposeService(discoveryService);
        }
    }

    private bool IsPendingRecoveryPathSafeForOpen(
        PendingPullRecoveryOpenSelection selection)
    {
        try
        {
            EnsurePendingRecoveryPathIsSafeForOpen(
                selection.Repository,
                selection.Recovery,
                selection.ProjectFile);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "The recovered project path {ProjectFile} is not safe to open.",
                selection.ProjectFile);
            return false;
        }
    }

    private async Task CompleteOpeningPullRecoveryAfterPublishedAsync(Project project)
    {
        string projectFile = GetProjectFile(project);
        string canonicalProjectFile = GetOpeningRecoveryKey(projectFile);
        PendingOpeningPullRecovery? prepared;
        lock (_stateGate)
        {
            _openingPullRecoveries.TryGetValue(canonicalProjectFile, out prepared);
        }

        if (prepared is null)
        {
            return;
        }

        IProjectVersionControlBackend? service = null;
        try
        {
            service = CreateTemporaryBackend(prepared.Repository, projectFile);
            await service.ExecuteExclusiveAsync(
                    async transaction =>
                    {
                        PendingPullRecovery current = await GetUnchangedPendingPullRecoveryAsync(
                            transaction,
                            prepared.Recovery,
                            prepared.Repository,
                            projectFile,
                            CancellationToken.None);
                        await transaction.CompletePendingPullRecoveryAsync(
                            current,
                            CancellationToken.None);
                        return true;
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
            ForgetOpeningPullRecoveryMarker(canonicalProjectFile, prepared);
            CompletePendingPullRecoveryPublication(prepared.Recovery.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "The project opened after pending pull recovery, but its descriptor could not be completed.");
        }
        finally
        {
            DisposeService(service);
        }
    }

    // The recovery a transaction acts on must still be the one this open inspected.
    private static async Task<PendingPullRecovery> GetUnchangedPendingPullRecoveryAsync(
        IProjectVersionControlTransaction transaction,
        PendingPullRecovery expected,
        RepositoryInfo repository,
        string projectFile,
        CancellationToken cancellationToken)
    {
        PendingPullRecovery? current = await FindPendingPullRecoveryAsync(
            transaction,
            expected.Id,
            cancellationToken);
        if (current is null
            || !PendingPullRecoveriesMatch(
                expected,
                current,
                repository)
            || !RecoveryProjectPathsEqual(
                repository,
                current.ProjectFile,
                projectFile))
        {
            throw new PendingPullRecoveryChangedException(
                expected.DescriptorRef);
        }

        return current;
    }

    private void ForgetOpeningPullRecoveryMarker(
        string canonicalProjectFile,
        PendingOpeningPullRecovery expected)
    {
        lock (_stateGate)
        {
            if (_openingPullRecoveries.TryGetValue(
                    canonicalProjectFile,
                    out PendingOpeningPullRecovery? current)
                && ReferenceEquals(current, expected))
            {
                _openingPullRecoveries.Remove(canonicalProjectFile);
            }
        }
    }

    private PendingOpeningPullRecovery? FindAppliedOpeningPullRecoveryMarker(
        string canonicalProjectFile,
        RepositoryInfo repository,
        PendingPullRecovery recovery)
    {
        lock (_stateGate)
        {
            if (_openingPullRecoveries.TryGetValue(
                    canonicalProjectFile,
                    out PendingOpeningPullRecovery? liveMarker)
                && liveMarker is not null
                && RepositoriesEqual(liveMarker.Repository, repository)
                && PendingPullRecoveriesMatch(
                    liveMarker.Recovery,
                    recovery,
                    repository))
            {
                return liveMarker;
            }
        }

        return null;
    }

    private bool IsAppliedOpeningPullRecoveryMarkerCurrent(
        string canonicalProjectFile,
        PendingPullRecoveryOpenSelection selection,
        RepositoryInfo repository)
    {
        lock (_stateGate)
        {
            return _openingPullRecoveries.TryGetValue(
                       canonicalProjectFile,
                       out PendingOpeningPullRecovery? liveMarker)
                   && liveMarker is not null
                   && ReferenceEquals(
                       liveMarker,
                       selection.AppliedMarker)
                   && liveMarker.Repository.Equals(repository)
                   && PendingPullRecoveriesMatch(
                       liveMarker.Recovery,
                       selection.Recovery,
                       repository);
        }
    }

    private sealed record PendingOpeningPullRecovery(
        RepositoryInfo Repository,
        PendingPullRecovery Recovery);

    private sealed record PendingPullRecoveryOpenSelection(
        RepositoryInfo Repository,
        PendingPullRecovery Recovery,
        string ProjectFile,
        bool Accepted,
        PendingOpeningPullRecovery? AppliedMarker)
    {
        public bool AlreadyApplied => AppliedMarker is not null;
    }
}
