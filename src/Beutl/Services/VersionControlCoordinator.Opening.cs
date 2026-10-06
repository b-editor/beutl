using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private async Task<ProjectService.ProjectOpenPreparation?> PrepareProjectOpeningAsync(
        ProjectService.ProjectOpenAttempt attempt,
        CancellationToken cancellationToken)
    {
        lock (_stateGate)
        {
            if (_pendingOpeningRepositoryDecision is { } pending
                && !ReferenceEquals(pending.Attempt, attempt))
            {
                _pendingOpeningRepositoryDecision = null;
            }
        }

        using NonTransactionalOperationLease? operation =
            TryBeginNonTransactionalOperation(cancellationToken);
        if (operation is null)
        {
            return new AbortProjectOpenPreparation();
        }

        CancellationToken operationCancellation = operation.CancellationToken;
        OpeningRepositoryInspection? inspection = null;
        try
        {
            inspection = await DiscoverPendingPullRecoveryForOpeningAsync(
                    attempt.ProjectFile,
                    operationCancellation)
                .ConfigureAwait(false);
            if (inspection is null
                || !inspection.Repository.IsNestedInForeignRepo
                && inspection.Recovery is null)
            {
                return null;
            }

            PendingPullRecoveryOpenSelection? selection = inspection.Recovery;
            bool accepted = selection is null
                            || selection.AlreadyApplied
                            || await ConfirmPendingPullRecoveryAsync(
                                ToRecoveryInfo(selection.Recovery),
                                operationCancellation);
            var preparedInspection = inspection with
            {
                Recovery = selection is null
                    ? null
                    : selection with { Accepted = accepted },
            };

            if (preparedInspection.Recovery is
                {
                    Accepted: true,
                    AlreadyApplied: false,
                } recovery
                && _projectService.CurrentProject.Value?.Uri?.LocalPath is { } currentProjectFile
                && RecoveryProjectPathsEqual(
                    preparedInspection.Repository,
                    currentProjectFile,
                    recovery.ProjectFile))
            {
                // The existing recovery cycle needs to acquire the lifecycle and project-transition
                // gates, so release this preflight operation before entering it.
                operation.Dispose();
                // The normal open transition applies preparations before closing the current
                // project. Reuse the version-control recovery cycle for this same-project case so
                // stale in-memory state cannot be saved over the recovered files afterward.
                CancelPendingPullRecoveryOffer();
                await RunPendingPullRecoveryCycleAsync(
                        recovery.Recovery.Id,
                        requireConfirmation: false,
                        cancellationToken,
                        recovery.Recovery,
                        attempt)
                    .ConfigureAwait(false);
                return new AbortProjectOpenPreparation();
            }

            return new VersionControlProjectOpenPreparation(
                this,
                attempt,
                preparedInspection);
        }
        catch (OperationCanceledException) when (operationCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to inspect pending pull recovery before opening {ProjectFile}.",
                attempt.ProjectFile);
            PublishNotification(() =>
                NotificationService.ShowError(
                    Strings.VersionControl_ErrorTitle,
                    string.Format(
                        Strings.VersionControl_OpenAbortedFormat,
                        GetErrorText(ex))));
            return new AbortProjectOpenPreparation();
        }
    }

    private async Task InspectProjectOpeningAsync(string projectFile)
    {
        using NonTransactionalOperationLease? operation =
            TryBeginNonTransactionalOperation(CancellationToken.None);
        if (operation is null)
        {
            return;
        }

        CancellationToken cancellationToken = operation.CancellationToken;
        string? markerFile = await ProjectConflictMarkerScanner.FindFirstAsync(
            projectFile,
            cancellationToken);
        if (markerFile is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WarnConflictMarkersAsync(markerFile);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private async Task<ProjectOpenPreparationResult> ApplyProjectOpeningPreparationAsync(
        ProjectService.ProjectOpenAttempt attempt,
        OpeningRepositoryInspection inspection,
        ProjectTransitionContext transition,
        CancellationToken cancellationToken)
    {
        try
        {
            if (transition.Purpose != ProjectTransitionPurpose.Normal
                || !ReferenceEquals(transition.Owner, attempt)
                || !PathsEqual(attempt.ProjectFile, inspection.ProjectFile))
            {
                return ProjectOpenPreparationResult.Abort;
            }

            if (inspection.Recovery is { } recovery)
            {
                ProjectOpenPreparationResult result =
                    await ApplyPendingPullRecoveryBeforeOpeningAsync(
                            recovery,
                            cancellationToken)
                        .ConfigureAwait(false);
                if (result == ProjectOpenPreparationResult.Abort)
                {
                    return result;
                }
            }
            else
            {
                RepositoryInfo? current = await RevalidateOpeningRepositoryAsync(
                        inspection.ProjectFile,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (current is null || !RepositoriesEqual(current, inspection.Repository))
                {
                    return ProjectOpenPreparationResult.Proceed;
                }
            }

            if (!inspection.Repository.IsNestedInForeignRepo)
            {
                return ProjectOpenPreparationResult.Proceed;
            }

            return TryRecordOpeningRepositoryDecision(
                attempt,
                transition,
                inspection)
                ? ProjectOpenPreparationResult.Proceed
                : ProjectOpenPreparationResult.Abort;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ProjectOpenPreparationResult.Abort;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to revalidate enclosing-repository consent for project open {ProjectFile}.",
                inspection.ProjectFile);
            return ProjectOpenPreparationResult.Abort;
        }
    }

    private async Task<RepositoryInfo?> RevalidateOpeningRepositoryAsync(
        string projectFile,
        CancellationToken cancellationToken)
    {
        using NonTransactionalOperationLease? operation =
            TryBeginNonTransactionalOperation(cancellationToken);
        if (operation is null)
        {
            return null;
        }

        IProjectVersionControlBackend? discoveryService = null;
        try
        {
            discoveryService = CreateTemporaryBackend(repository: null, projectFile);
            GitAvailability availability = await discoveryService.GetAvailabilityAsync(
                    operation.CancellationToken)
                .ConfigureAwait(false);
            if (availability.State != GitAvailabilityState.Installed)
            {
                return null;
            }

            string projectRoot = Path.GetDirectoryName(projectFile)
                                 ?? throw new InvalidOperationException(
                                     "The project file has no parent directory.");
            return await discoveryService.DiscoverRepositoryAsync(
                    projectRoot,
                    operation.CancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            DisposeService(discoveryService);
        }
    }

    private bool TryRecordOpeningRepositoryDecision(
        ProjectService.ProjectOpenAttempt attempt,
        ProjectTransitionContext transition,
        OpeningRepositoryInspection inspection)
    {
        if (transition.Purpose != ProjectTransitionPurpose.Normal
            || !ReferenceEquals(transition.Owner, attempt)
            || !PathsEqual(attempt.ProjectFile, inspection.ProjectFile)
            || !VersionControlPathComparison.AreSameCanonicalPath(
                inspection.Repository.ProjectRoot,
                Path.GetDirectoryName(inspection.ProjectFile)
                ?? throw new InvalidOperationException(
                    "The project file has no parent directory.")))
        {
            return false;
        }

        lock (_stateGate)
        {
            if (_disposed)
            {
                return false;
            }

            _pendingOpeningRepositoryDecision = new PendingOpeningRepositoryDecision(
                attempt,
                attempt.Id,
                transition.Id,
                GetOpeningRecoveryKey(inspection.ProjectFile),
                inspection.Repository,
                inspection.EnclosingRepositoryAccepted);
            return true;
        }
    }

    private IProjectVersionControlBackend CreateTemporaryBackend(
        RepositoryInfo? repository,
        string projectFile)
    {
        return _serviceFactory?.Invoke(repository)
               ?? new GitCliVersionControlService(
                   _installationLocator,
                   repository,
                   () => _projectService.CurrentProject.Value is not { } project
                         || !PathsEqual(GetProjectFile(project), projectFile),
                   PresentPolicyNoticeAsync,
                   projectFile,
                   RequestIdentityForSnapshotAsync);
    }

    private PendingOpeningRepositoryDecision? TryTakeOpeningRepositoryDecision(Project project)
    {
        ProjectTransitionContext? transition = _projectService.CurrentTransition;
        string projectFile = GetProjectFile(project);
        string projectRoot = GetProjectRoot(project);
        lock (_stateGate)
        {
            if (_pendingOpeningRepositoryDecision is not { } pending)
            {
                return null;
            }

            bool matches = false;
            try
            {
                matches = transition is
                {
                    Purpose: ProjectTransitionPurpose.Normal,
                    Owner: ProjectService.ProjectOpenAttempt attempt,
                }
                && ReferenceEquals(pending.Attempt, attempt)
                && pending.AttemptId == attempt.Id
                && pending.TransitionId == transition.Id
                && PathsEqual(pending.ProjectFile, projectFile)
                && VersionControlPathComparison.AreSameCanonicalPath(
                    pending.Repository.ProjectRoot,
                    projectRoot);
            }
            catch (Exception ex)
                when (ex is IOException
                      or UnauthorizedAccessException
                      or NotSupportedException
                      or ArgumentException)
            {
                _logger.LogWarning(
                    ex,
                    "Could not match enclosing-repository consent to the opening project {ProjectFile}.",
                    projectFile);
            }

            _pendingOpeningRepositoryDecision = null;
            return matches ? pending : null;
        }
    }

    private sealed record PendingOpeningRepositoryDecision(
        ProjectService.ProjectOpenAttempt Attempt,
        long AttemptId,
        long TransitionId,
        string ProjectFile,
        RepositoryInfo Repository,
        bool Accepted);

    private sealed record OpeningRepositoryInspection(
        RepositoryInfo Repository,
        string ProjectFile,
        bool EnclosingRepositoryAccepted,
        PendingPullRecoveryOpenSelection? Recovery);

    private sealed class VersionControlProjectOpenPreparation(
        VersionControlCoordinator owner,
        ProjectService.ProjectOpenAttempt attempt,
        OpeningRepositoryInspection inspection)
        : ProjectService.ProjectOpenPreparation
    {
        internal override Task<ProjectOpenPreparationResult> ApplyAsync(
            ProjectTransitionContext transition,
            CancellationToken cancellationToken)
        {
            return owner.ApplyProjectOpeningPreparationAsync(
                attempt,
                inspection,
                transition,
                cancellationToken);
        }
    }

    private sealed class AbortProjectOpenPreparation : ProjectService.ProjectOpenPreparation
    {
        internal override Task<ProjectOpenPreparationResult> ApplyAsync(
            ProjectTransitionContext transition,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ProjectOpenPreparationResult.Abort);
        }
    }
}
