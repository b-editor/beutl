using Avalonia.Threading;
using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    internal void OnProjectChanged(Project? project)
    {
        // The transition is read where it is committed, since it can be over by the time a call from
        // another thread reaches the UI thread.
        ProjectTransitionContext? transition = _projectService.CurrentTransition;
        if (_dispatcher.CheckAccess())
        {
            OnProjectChanged(project, transition);
        }
        else
        {
            _dispatcher.Post(() => OnProjectChanged(project, transition), DispatcherPriority.Normal);
        }
    }

    private void OnProjectChanged(Project? project, ProjectTransitionContext? transition)
    {
        if (_disposed)
        {
            return;
        }

        _pendingConfigurationActivation = null;
        AdvanceProjectServiceEpoch();
        if (IsInternalVersionControlTransition(transition))
        {
            ShowProjectAfterInternalTransition(project);
            return;
        }

        _repositoryHygieneConfigurationDirty = false;
        bool newProject = project is not null && IsProjectCreationTransition(transition);
        ActivateProject(
            project,
            newProject,
            newProject ? TakePreparedNewProject(project!, transition) : null,
            CancellationToken.None);
    }

    // A version-control operation closed and reopened the project, and the backend it ran with goes on
    // tracking the project it reopened.
    private void ShowProjectAfterInternalTransition(Project? project)
    {
        if (project is null)
        {
            SetVisibleService(null);
            return;
        }

        try
        {
            if (GetOwnedBackend() is { Repository: { } repository } preservedService
                && PathsEqual(repository.ProjectRoot, GetProjectRoot(project)))
            {
                SetVisibleService(preservedService);
                QueueRepositoryHygieneConfigurationIfDirty(project);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate version control for the open project.");
            ClearProjectState();
            return;
        }

        ActivateProject(project, newProject: false, preparedNewProject: null, CancellationToken.None);
    }

    // Starts deciding afresh how the project is tracked: an untracked backend shows the project at once,
    // and the activation looks for its repository in the background. A backend that preparedNewProject
    // carries already initialized the new project's repository, and the activation adopts it in place of
    // a fresh untracked backend. Returns null when no activation starts.
    private ActivationContext? ActivateProject(
        Project? project,
        bool newProject,
        PreparedNewProject? preparedNewProject,
        CancellationToken cancellationToken,
        bool isRediscovery = false)
    {
        IProjectVersionControlBackend? preparedService = preparedNewProject?.Service;
        try
        {
            if (_disposed)
            {
                return null;
            }

            if (project is null)
            {
                ClearProjectState();
                return null;
            }

            if (!ReferenceEquals(_projectService.CurrentProject.Value, project))
            {
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
            preparedService = null;
            var activation = new ActivationContext(
                projectRoot,
                projectFile,
                service,
                newProject,
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _lifetimeCancellation.Token))
            {
                IsRediscovery = isRediscovery,
                InterruptedNewProjectRepository = preparedNewProject?.InterruptedRepository,
            };
            ActivationContext? previousActivation = _activation;
            _activation = activation;
            TransitionOwnedService(service, service, projectRoot, previousActivation);
            CancelActivation(previousActivation);
            _ = ActivateRepositoryAsync(activation);
            return activation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate version control for the open project.");
            ClearProjectState();
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

    private async Task ActivateRepositoryAsync(ActivationContext activation)
    {
        using RunningWork work = BeginWork();
        try
        {
            RepositoryInfo? repository = await DiscoverActivationRepositoryAsync(activation);
            if (repository is null
                || !await IsActivationRepositoryAcceptedAsync(activation, repository)
                || !IsCurrentActivation(activation))
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
            activation.Candidate = trackedService;
            try
            {
                await trackedService.EnsureRepositoryHygieneAsync(activation.CancellationToken);
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
                if (IsCurrentActivation(activation))
                {
                    // The project stays open untracked, unless the factory handed out one backend for both
                    // roles, which cannot go on as the untracked one.
                    if (ReferenceEquals(trackedService, activation.Service))
                    {
                        ClearProjectState();
                    }

                    // The warning carries the reason (for example a project path the repository ignores,
                    // or Git's own error) instead of leaving it only in the log.
                    PublishNotification(() =>
                        NotificationService.ShowWarning(
                            Strings.VersionControl,
                            string.Format(Strings.VersionControl_ActivationFailedFormat, GetErrorText(ex))));
                }

                throw;
            }

            if (IsCurrentActivation(activation))
            {
                TransitionOwnedService(trackedService, trackedService, activation.ProjectRoot, activation);
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
            if (ReferenceEquals(_activation, activation))
            {
                _activation = null;
            }

            IProjectVersionControlBackend? candidate = activation.Candidate;
            activation.Finish();
            // The untracked backend leaves through the state like any owned backend; a tracked backend
            // the state never took is retired here.
            if (candidate is not null && !ReferenceEquals(candidate, activation.Service))
            {
                RetireIfUnused(candidate);
            }
        }
    }

    // Null when Git is missing, a new project's own decision stands, Git cannot inspect the folder, or
    // there is no repository with a checked-out commit to track.
    private async Task<RepositoryInfo?> DiscoverActivationRepositoryAsync(
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

        RepositoryInfo? repository;
        try
        {
            repository = await activation.Service.DiscoverRepositoryAsync(
                activation.ProjectRoot,
                activation.CancellationToken);
        }
        catch (ArgumentException ex) when (activation.ProjectRoot.Any(char.IsControl))
        {
            // Git commands cannot safely carry control-character repository paths through the
            // line-oriented discovery protocol. Version control is optional, so the project stays
            // untracked.
            _logger.LogInformation(
                ex,
                "Opened {ProjectFile} without version control because its path is unsupported by repository discovery.",
                activation.ProjectFile);
            return null;
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            // Git refused the folder itself (for example dubious ownership on a shared or
            // removable volume). The project stays open without version control and says why.
            _logger.LogWarning(
                ex,
                "Opened {ProjectFile} without version control because repository discovery failed.",
                activation.ProjectFile);
            if (IsCurrentActivation(activation))
            {
                PublishNotification(() =>
                    NotificationService.ShowWarning(
                        Strings.VersionControl,
                        string.Format(
                            Strings.VersionControl_OpenedWithoutVersionControlFormat,
                            GetErrorText(ex))));
            }

            return null;
        }

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
            if (!await ConfirmUseEnclosingRepositoryIfNeededAsync(
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
