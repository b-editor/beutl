using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    internal void OnProjectChanged(Project? project)
    {
        bool internalTransition = IsInternalVersionControlTransition();
        PendingOpeningRepositoryDecision? openingRepositoryDecision =
            project is null || internalTransition
                ? null
                : TryTakeOpeningRepositoryDecision(project);
        bool newProject = project is not null && !internalTransition && IsProjectCreationTransition();
        CancellationTokenSource? configurationActivationCancellation;
        long activationRevision;
        PreparedNewProject? preparedNewProject = null;
        lock (_stateGate)
        {
            _lastProjectNotification = project;
            _hasProjectNotification = true;
            if (!TryBeginProjectActivationLocked(
                    internalTransition,
                    out configurationActivationCancellation,
                    out activationRevision))
            {
                return;
            }

            if (newProject)
            {
                preparedNewProject = TakePreparedNewProjectLocked(project!);
            }
        }

        AdvanceProjectServiceEpoch();
        CancelConfigurationActivation(configurationActivationCancellation);
        if (!internalTransition)
        {
            CancelPendingPullRecoveryOffer();
        }
        StartProjectActivation(
            project,
            internalTransition,
            activationRevision,
            openingRepositoryDecision,
            newProject,
            preparedNewProject);
    }

    private void ObserveCurrentProjectSnapshot()
    {
        bool internalTransition = IsInternalVersionControlTransition();
        CancellationTokenSource? configurationActivationCancellation;
        Project? project;
        long activationRevision;
        lock (_stateGate)
        {
            project = _projectService.CurrentProject.Value;
            if (_hasProjectNotification
                && ReferenceEquals(_lastProjectNotification, project))
            {
                return;
            }

            if (!TryBeginProjectActivationLocked(
                    internalTransition,
                    out configurationActivationCancellation,
                    out activationRevision))
            {
                return;
            }
        }

        CancelConfigurationActivation(configurationActivationCancellation);
        if (!internalTransition)
        {
            CancelPendingPullRecoveryOffer();
        }
        StartProjectActivation(
            project,
            internalTransition,
            activationRevision,
            openingRepositoryDecision: null,
            newProject: false,
            preparedNewProject: null);
    }

    private void StartProjectActivation(
        Project? project,
        bool internalTransition,
        long activationRevision,
        PendingOpeningRepositoryDecision? openingRepositoryDecision,
        bool newProject,
        PreparedNewProject? preparedNewProject)
    {
        _ = StartProjectActivationAfterOpeningRecoveryAsync(
            project,
            internalTransition,
            activationRevision,
            openingRepositoryDecision,
            newProject,
            preparedNewProject);
    }

    // Owns the prepared backend until OnProjectChangedAsync takes it over.
    private async Task StartProjectActivationAfterOpeningRecoveryAsync(
        Project? project,
        bool internalTransition,
        long activationRevision,
        PendingOpeningRepositoryDecision? openingRepositoryDecision,
        bool newProject,
        PreparedNewProject? preparedNewProject)
    {
        IProjectVersionControlBackend? preparedService = preparedNewProject?.Service;
        try
        {
            if (!ReferenceEquals(_projectService.CurrentProject.Value, project))
            {
                return;
            }

            if (project is not null)
            {
                await CompleteOpeningPullRecoveryAfterPublishedAsync(project).ConfigureAwait(false);
                if (!ReferenceEquals(_projectService.CurrentProject.Value, project))
                {
                    return;
                }
            }

            preparedService = null;
            await OnProjectChangedAsync(
                    project,
                    internalTransition,
                    activationRevision,
                    openingRepositoryDecision,
                    newProject,
                    CancellationToken.None,
                    preparedNewProject)
                .ConfigureAwait(false);
        }
        finally
        {
            if (preparedService is not null)
            {
                DiscardNewProjectBackend(preparedService);
            }

            FinishActivationSetup();
        }
    }

    private async Task<ActivationContext?> StartProjectActivationAsync(
        Project? project,
        bool internalTransition,
        CancellationToken cancellationToken)
    {
        if (!TryBeginActivationSetup(internalTransition, out long activationRevision))
        {
            return null;
        }

        try
        {
            return await OnProjectChangedAsync(
                    project,
                    internalTransition,
                    activationRevision,
                    openingRepositoryDecision: null,
                    newProject: false,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            FinishActivationSetup();
        }
    }

    // A backend that preparedNewProject carries already initialized the new project's repository, and the
    // activation adopts it in place of a fresh untracked backend.
    private async Task<ActivationContext?> OnProjectChangedAsync(
        Project? project,
        bool internalTransition,
        long activationRevision,
        PendingOpeningRepositoryDecision? openingRepositoryDecision,
        bool newProject,
        CancellationToken cancellationToken,
        PreparedNewProject? preparedNewProject = null)
    {
        IProjectVersionControlBackend? preparedService = preparedNewProject?.Service;
        try
        {
            if (internalTransition && !TryPromoteActivationRevision(activationRevision))
            {
                return null;
            }

            if (internalTransition)
            {
                if (project is null)
                {
                    SetVisibleService(null);
                    return null;
                }

                string preservedRoot = GetProjectRoot(project);
                IProjectVersionControlBackend? preservedService = GetOwnedBackend();
                if (preservedService?.Repository is { } preservedRepository
                    && VersionControlPathComparison.AreSameCanonicalPath(
                        preservedRepository.ProjectRoot,
                        preservedRoot))
                {
                    SetVisibleService(preservedService);
                    QueueRepositoryHygieneConfigurationIfDirty(project);
                    return null;
                }
            }

            if (project is null)
            {
                ClearProjectState(activationRevision);
                return null;
            }

            string projectRoot = GetProjectRoot(project);
            string projectFile = GetProjectFile(project);
            IProjectVersionControlBackend service = preparedService
                ?? _serviceFactory?.Invoke(null)
                ?? new GitCliVersionControlService(
                    _installationLocator,
                    repository: null,
                    () => _projectService.CurrentProject.Value is null,
                    PresentPolicyNoticeAsync,
                    projectFile);
            // From here the activation owns the prepared backend, and a rejected activation retires it.
            preparedService = null;
            var activation = new ActivationContext(
                activationRevision,
                projectRoot,
                projectFile,
                service,
                openingRepositoryDecision,
                newProject,
                cancellationToken)
            {
                InterruptedNewProjectRepository = preparedNewProject?.InterruptedRepository,
            };
            if (BeginActivation(activation, out bool cleanupRejectedService))
            {
                _ = ActivateRepositoryAsync(activation);
                return activation;
            }

            await CompleteRejectedActivationAsync(activation, cleanupRejectedService)
                .ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate version control for the open project.");
            ClearProjectState(activationRevision);
            return null;
        }
        finally
        {
            if (preparedService is not null)
            {
                DiscardNewProjectBackend(preparedService);
            }
        }
    }

    private bool TryBeginProjectActivationLocked(
        bool internalTransition,
        out CancellationTokenSource? configurationActivationCancellation,
        out long activationRevision)
    {
        _pendingConfigurationActivation = null;
        if (!internalTransition)
        {
            _repositoryHygieneConfigurationDirty = false;
        }

        configurationActivationCancellation = _configurationActivationCancellation;
        return TryBeginActivationSetupLocked(
            internalTransition,
            out activationRevision);
    }

    private bool TryBeginActivationSetup(
        bool internalTransition,
        out long activationRevision)
    {
        lock (_stateGate)
        {
            return TryBeginActivationSetupLocked(
                internalTransition,
                out activationRevision);
        }
    }

    private bool TryBeginActivationSetupLocked(
        bool internalTransition,
        out long activationRevision)
    {
        if (_disposed)
        {
            activationRevision = 0;
            return false;
        }

        _activationSetupUsers++;
        activationRevision = ++_nextActivationRevision;
        if (!internalTransition)
        {
            _latestActivationRevision = activationRevision;
        }

        return true;
    }

    private bool TryPromoteActivationRevision(long activationRevision)
    {
        lock (_stateGate)
        {
            if (_disposed || activationRevision < _latestActivationRevision)
            {
                return false;
            }

            _latestActivationRevision = activationRevision;
            return true;
        }
    }

    private void FinishActivationSetup()
    {
        TaskCompletionSource? quiesced = null;
        lock (_stateGate)
        {
            _activationSetupUsers--;
            if (_activationSetupUsers == 0 && _disposed)
            {
                quiesced = _activationSetupsQuiesced;
            }
        }

        quiesced?.TrySetResult();
        TryStartPendingConfigurationActivation();
    }

    private async Task CompleteRejectedActivationAsync(
        ActivationContext activation,
        bool cleanupService)
    {
        try
        {
            CancelActivation(activation);
            activation.Complete();
            await activation.CancellationQuiesced.ConfigureAwait(false);
            if (cleanupService)
            {
                await RetireDiscardedServiceAsync(
                        activation,
                        activation.Service,
                        cleanupAlreadyClaimed: true)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            activation.Finish();
        }
    }

    private async Task ActivateRepositoryAsync(ActivationContext activation)
    {
        IProjectVersionControlBackend? candidateService = null;
        IProjectVersionControlBackend? pendingCleanup = null;
        IProjectVersionControlBackend? pendingRecoveryOfferService = null;
        try
        {
            await activation.PredecessorsCompleted.ConfigureAwait(false);
            activation.CancellationToken.ThrowIfCancellationRequested();
            if (!TryPublishActivationServiceIfCurrent(activation))
            {
                return;
            }

            RepositoryInfo? repository = await DiscoverActivationRepositoryAsync(activation);
            if (repository is null)
            {
                return;
            }

            if (!await IsActivationRepositoryAcceptedAsync(activation, repository))
            {
                return;
            }

            if (!IsCurrentActivation(activation))
            {
                return;
            }

            IProjectVersionControlBackend trackedService = _serviceFactory?.Invoke(repository)
                ?? new GitCliVersionControlService(
                    _installationLocator,
                    repository,
                    () => _projectService.CurrentProject.Value is null,
                    PresentPolicyNoticeAsync,
                    activation.ProjectFile,
                    RequestIdentityForSnapshotAsync);
            candidateService = trackedService;
            if (!TryRegisterCandidateService(activation, trackedService))
            {
                pendingCleanup = trackedService;
                return;
            }

            await activation.PredecessorsCompleted.ConfigureAwait(false);
            activation.CancellationToken.ThrowIfCancellationRequested();

            try
            {
                await trackedService.EnsureRepositoryHygieneAsync(
                    activation.CancellationToken);
            }
            catch (Exception ex)
                when (ex is VersionControlConflictedException or DetachedHeadNotSupportedException
                      && !activation.CancellationToken.IsCancellationRequested)
            {
                // Git keeps working in these states, so the project stays tracked and the tab explains
                // what to resolve outside Beutl. The backend refuses every write until then and
                // finishes the skipped hygiene before its next commit.
                _logger.LogInformation(
                    ex,
                    "Opened a repository that needs attention in an external Git tool before Beutl can update it.");
            }
            catch (Exception ex)
            {
                bool notify = !activation.CancellationToken.IsCancellationRequested && IsCurrentActivation(activation);
                if (activation.OwnsService(trackedService))
                {
                    ClearProjectState(activation.Revision);
                }
                else
                {
                    pendingCleanup = trackedService;
                }

                // The project stays open untracked, so the warning carries the reason (for example a
                // project path the repository ignores, or Git's own error) instead of leaving it only
                // in the log.
                if (notify)
                    PublishNotification(
                        () => NotificationService.ShowWarning(
                            Strings.VersionControl,
                            string.Format(Strings.VersionControl_ActivationFailedFormat, GetErrorText(ex))),
                        activation.Revision);
                throw;
            }

            bool activationCompleted = CompleteActivation(activation, trackedService);
            if (!activationCompleted && !activation.OwnsService(trackedService))
            {
                pendingCleanup = trackedService;
            }

            if (activationCompleted)
            {
                pendingRecoveryOfferService = trackedService;
            }
        }
        catch (OperationCanceledException) when (activation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover version control for the open project.");
        }
        finally
        {
            await FinishRepositoryActivationAsync(
                    activation,
                    pendingCleanup,
                    candidateService,
                    pendingRecoveryOfferService)
                .ConfigureAwait(false);
        }
    }

    // Null when Git is missing, a new project's own decision stands, or there is no repository with a
    // checked-out commit to track.
    private static async Task<RepositoryInfo?> DiscoverActivationRepositoryAsync(
        ActivationContext activation)
    {
        GitAvailability availability = await activation.Service.GetAvailabilityAsync(
            activation.CancellationToken);
        // A project created in this session opens with the backend its creation left: tracked when the
        // new-project dialog chose tracking and initialization recorded the first version, untracked
        // otherwise. The dialog already decided, so discovery neither adopts an enclosing repository
        // nor asks again; initialization did both before the project opened. Discovery only looks
        // again at a repository that initialization created or attached before it stopped.
        if (availability.State != GitAvailabilityState.Installed
            || activation.IsNewProject && activation.InterruptedNewProjectRepository is null)
        {
            return null;
        }

        RepositoryInfo? repository = await activation.Service.DiscoverRepositoryAsync(
            activation.ProjectRoot,
            activation.CancellationToken);
        if (repository is null)
        {
            return null;
        }

        // Hygiene needs a checked-out commit, so a branch with none yet, such as a bare
        // `git init`, can neither resume nor adopt tracking. Leaving the project untracked
        // offers initialization instead, which accepts the unborn branch.
        if (!await activation.Service.HasCheckedOutCommitAsync(
                repository,
                activation.CancellationToken))
        {
            return null;
        }

        return repository;
    }

    private async Task<bool> IsActivationRepositoryAcceptedAsync(
        ActivationContext activation,
        RepositoryInfo repository)
    {
        if (activation.InterruptedNewProjectRepository is { } interruptedRepository)
        {
            // The dialog chose tracking and consented to the repository initialization reached, so once it
            // has a checked-out commit it resumes tracking without asking again, but only if discovery
            // still finds that same repository.
            if (!RepositoriesEqual(interruptedRepository, repository))
            {
                return false;
            }
        }
        else if (repository.IsNestedInForeignRepo)
        {
            PendingOpeningRepositoryDecision? openingDecision =
                activation.OpeningRepositoryDecision;
            bool matchesOpeningDecision = openingDecision is not null
                                          && RepositoriesEqual(
                                              openingDecision.Repository,
                                              repository)
                                          && VersionControlPathComparison.AreSameCanonicalPath(
                                              repository.ProjectRoot,
                                              activation.ProjectRoot);
            if (matchesOpeningDecision)
            {
                if (!openingDecision!.Accepted)
                {
                    return false;
                }
            }
            else if (!await ConfirmUseEnclosingRepositoryIfNeededAsync(
                         activation.Service,
                         repository,
                         activation.CancellationToken))
            {
                return false;
            }
        }
        else if (!await ConfirmAdoptRepositoryIfNeededAsync(
                     activation.Service,
                     repository,
                     activation.CancellationToken))
        {
            return false;
        }

        return true;
    }

    private async Task FinishRepositoryActivationAsync(
        ActivationContext activation,
        IProjectVersionControlBackend? pendingCleanup,
        IProjectVersionControlBackend? candidateService,
        IProjectVersionControlBackend? pendingRecoveryOfferService)
    {
        try
        {
            activation.Complete();
            await activation.CancellationQuiesced.ConfigureAwait(false);
            await activation.PredecessorsCompleted.ConfigureAwait(false);
            if (pendingCleanup is not null)
            {
                await RetireDiscardedServiceAsync(activation, pendingCleanup)
                    .ConfigureAwait(false);
            }
            else if (candidateService is not null)
            {
                if (activation.OwnsService(candidateService))
                {
                    UnregisterCandidateService(activation, candidateService);
                }
                else
                {
                    await RetireDiscardedServiceAsync(activation, candidateService)
                        .ConfigureAwait(false);
                }
            }

            bool stillOwned;
            lock (_stateGate)
            {
                if (ReferenceEquals(_activation, activation))
                {
                    _activation = null;
                }

                stillOwned = ReferenceEquals(_state.OwnedService, activation.Service);
            }

            if (!stillOwned)
            {
                await RetireDiscardedServiceAsync(activation, activation.Service)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            activation.Finish();
            if (pendingRecoveryOfferService is not null)
            {
                StartPendingPullRecoveryOffer(pendingRecoveryOfferService);
            }

            TryStartPendingConfigurationActivation();
        }
    }

    // Version tracking stays opt-in per project, so merely opening a project whose directory the
    // user has already made a repository must not start writing hygiene files and commits. Only a
    // repository that already records an earlier opt-in resumes tracking without asking.
    // Adopting a foreign work tree is the user's call, but only the first time: a repository that
    // already records an opt-in resumes tracking without asking, exactly like a non-nested one.
    // Asking on every open would turn a dismissed prompt into a session with no snapshots at all.
    private async Task<bool> ConfirmUseEnclosingRepositoryIfNeededAsync(
        IProjectVersionControlBackend service,
        RepositoryInfo repository,
        CancellationToken cancellationToken)
    {
        return await service.HasVersionTrackingOptInAsync(repository, cancellationToken)
               || await ConfirmUseEnclosingRepositoryAsync(repository, cancellationToken);
    }

    private async Task<bool> ConfirmAdoptRepositoryIfNeededAsync(
        IProjectVersionControlBackend service,
        RepositoryInfo repository,
        CancellationToken cancellationToken)
    {
        return await service.HasVersionTrackingOptInAsync(repository, cancellationToken)
               || await ConfirmAdoptExistingRepositoryAsync(repository, cancellationToken);
    }
}
