using Beutl.Editor.VersionControl;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    public async Task<bool> InitializeCurrentProjectAsync(
        Project expectedProject,
        Func<CancellationToken, Task<GitIdentity?>> requestIdentityAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedProject);
        ArgumentNullException.ThrowIfNull(requestIdentityAsync);
        using NonTransactionalOperationLease operation =
            await BeginNonTransactionalOperationAsync(cancellationToken);
        CancellationToken operationCancellation = operation.CancellationToken;

        string projectRoot = GetProjectRoot(expectedProject);
        Task activationTask;
        lock (_stateGate)
        {
            Project currentProject = _projectService.CurrentProject.Value
                                     ?? throw new InvalidOperationException("No project is open.");
            if (!ReferenceEquals(currentProject, expectedProject))
            {
                throw new InvalidOperationException(
                    "The requested project is no longer the open project.");
            }

            activationTask = _activation is { ProjectRoot: var activationRoot } activation
                             && PathsEqual(activationRoot, projectRoot)
                ? activation.Completion
                : Task.CompletedTask;
        }

        await activationTask.WaitAsync(operationCancellation);

        IProjectVersionControlBackend service;
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Project currentProject = _projectService.CurrentProject.Value
                                     ?? throw new InvalidOperationException(
                                         "The project was closed while version control was activating.");
            string currentRoot = GetProjectRoot(currentProject);
            if (!ReferenceEquals(currentProject, expectedProject)
                || !PathsEqual(currentRoot, projectRoot)
                || _state.ProjectRoot is not { } stateRoot
                || !PathsEqual(stateRoot, projectRoot))
            {
                throw new InvalidOperationException(
                    "The open project changed while version control was activating.");
            }

            service = _state.OwnedService
                      ?? throw new InvalidOperationException(
                          "The version control service is not available.");
        }

        RepositoryInfo? targetRepository = service.Repository
                                           ?? await SelectRepositoryForInitializationAsync(
                                               service,
                                               projectRoot,
                                               operationCancellation);
        if (targetRepository is null)
        {
            return false;
        }

        var options = new InitOptions(targetRepository, _config.UseLfsWhenAvailable);
        // InitializeAsync writes the hygiene files and stages the first revision, so it needs the
        // same workspace reservation as every later snapshot: without it an export or an auto-save
        // can still be writing and the initial commit captures a half-written tree.
        using IDisposable? initializationMutation = TryBeginWorktreeMutation();
        if (initializationMutation is null)
        {
            return false;
        }

        try
        {
            try
            {
                if (!await InitializeWithEditorSuspensionAsync(
                        service,
                        options,
                        expectedProject,
                        operationCancellation))
                {
                    return false;
                }
            }
            catch (GitIdentityRequiredException)
            {
                // InitializeWithEditorSuspensionAsync has released the editor and hidden the progress
                // view over it before this prompt. The user can keep editing while entering an identity;
                // the retry re-saves those edits after acquiring a fresh suspension.
                GitIdentity? identity = await requestIdentityAsync(operationCancellation);
                if (identity is null)
                {
                    return false;
                }

                operationCancellation.ThrowIfCancellationRequested();
                if (!await InitializeWithEditorSuspensionAsync(
                        service,
                        options with { Identity = identity },
                        expectedProject,
                        operationCancellation))
                {
                    return false;
                }
            }
        }
        catch (VersionControlConflictedException ex)
        {
            PublishNotification(() =>
                NotificationService.ShowWarning(Strings.VersionControl, ex.Guidance));
            return false;
        }

        // Runs after initialization, once the repository exists and is attached. Already-tracked
        // .beutl/*.tmp entries leave the repository permanently dirty for the pull precondition, but
        // a repository may be sharing them on purpose, so untracking them is the user's call.
        await UntrackReservedPathsIfConfirmedAsync(service, operationCancellation);

        bool schedulePublication;
        lock (_stateGate)
        {
            Project currentProject = _projectService.CurrentProject.Value
                                     ?? throw new InvalidOperationException(
                                         "The project was closed while version control was being initialized.");
            if (_disposed
                || !ReferenceEquals(currentProject, expectedProject)
                || !ReferenceEquals(_state.OwnedService, service)
                || _state.ProjectRoot is not { } stateRoot
                || !PathsEqual(stateRoot, projectRoot))
            {
                throw new InvalidOperationException(
                    "The open project changed while version control was being initialized.");
            }

            schedulePublication = TransitionStateLocked(
                _state with { IsTracked = service.Repository is not null });
        }

        SchedulePublicationDrain(schedulePublication);

        return true;
    }

    private async Task<bool> InitializeNewProjectAsync(
        NewProjectSetup setup,
        Project project,
        CancellationToken cancellationToken)
    {
        using NonTransactionalOperationLease operation =
            await BeginNonTransactionalOperationAsync(cancellationToken);
        CancellationToken operationCancellation = operation.CancellationToken;

        // Only the creation that wrote the project can hand it a repository, and only before the project
        // opens: from then on, the activation decides how the open project is tracked.
        if (_projectService.CurrentTransition is not
            {
                Purpose: ProjectTransitionPurpose.Normal,
                Owner: ProjectService.ProjectCreation,
            } transition)
        {
            throw new InvalidOperationException(
                "A new project can only be initialized while it is being created.");
        }

        if (_projectService.CurrentProject.Value is not null)
        {
            throw new InvalidOperationException(
                "A new project has to be initialized before it opens.");
        }

        string projectRoot = GetProjectRoot(project);
        IProjectVersionControlBackend service = CreateNewProjectBackend(GetProjectFile(project));
        bool prepared = false;
        try
        {
            RepositoryInfo? targetRepository = await SelectRepositoryForInitializationAsync(
                service,
                projectRoot,
                operationCancellation);
            if (targetRepository is null)
            {
                return false;
            }

            var options = new InitOptions(targetRepository, _config.UseLfsWhenAvailable);
            // Nothing can edit a project that is not open, but an export or a write that outlived the
            // previous project still has to finish before the first revision is staged.
            using IDisposable? initializationMutation = TryBeginWorktreeMutation();
            if (initializationMutation is null)
            {
                return false;
            }

            bool initialized = false;
            try
            {
                try
                {
                    try
                    {
                        await service.InitializeAsync(options, operationCancellation);
                    }
                    catch (GitIdentityRequiredException)
                    {
                        GitIdentity? identity = await setup.RequestIdentityAsync(operationCancellation);
                        if (identity is null)
                        {
                            return false;
                        }

                        operationCancellation.ThrowIfCancellationRequested();
                        await service.InitializeAsync(
                            options with { Identity = identity },
                            operationCancellation);
                    }

                    initialized = true;
                }
                catch (VersionControlConflictedException ex)
                {
                    PublishNotification(() =>
                        NotificationService.ShowWarning(Strings.VersionControl, ex.Guidance));
                    return false;
                }
            }
            finally
            {
                // Initialization can stop after it created or attached the repository, possibly with the
                // first version already recorded. The project then opens as reopening it would find it,
                // rather than untracked whatever is on disk.
                if (!initialized && service.Repository is not null)
                {
                    TryPrepareNewProject(setup, transition, project, service: null, targetRepository);
                }
            }

            // The first version is recorded, so the project opens tracked even if the step below fails.
            prepared = service.Repository is not null
                       && TryPrepareNewProject(setup, transition, project, service, repository: null);
            if (!prepared)
            {
                return false;
            }

            // An enclosing repository can already track Beutl's temporary files, and whether to stop
            // sharing them is the user's call, as when tracking is enabled for an open project.
            await UntrackReservedPathsIfConfirmedAsync(service, operationCancellation);

            return true;
        }
        finally
        {
            if (!prepared)
            {
                DiscardNewProjectBackend(service);
            }
        }
    }

    private IProjectVersionControlBackend CreateNewProjectBackend(string projectFile)
    {
        return _serviceFactory?.Invoke(null)
               ?? new GitCliVersionControlService(
                   _installationLocator,
                   repository: null,
                   () => _projectService.CurrentProject.Value is null,
                   PresentPolicyNoticeAsync,
                   projectFile,
                   RequestIdentityForSnapshotAsync);
    }

    private bool TryPrepareNewProject(
        NewProjectSetup setup,
        ProjectTransitionContext transition,
        Project project,
        IProjectVersionControlBackend? service,
        RepositoryInfo? repository)
    {
        PreparedNewProject? stale = null;
        lock (_stateGate)
        {
            if (_disposed
                || setup.IsDisposed
                || !ReferenceEquals(_projectService.CurrentTransition, transition)
                || _projectService.CurrentProject.Value is not null)
            {
                return false;
            }

            if (_preparedNewProject is { } existing)
            {
                if (ReferenceEquals(existing.Transition, transition))
                {
                    return false;
                }

                // A creation that ended without disposing its setup can no longer open its project.
                stale = existing;
            }

            _preparedNewProject = new PreparedNewProject(setup, transition, project, service, repository);
        }

        if (stale?.Service is { } staleService)
        {
            DiscardNewProjectBackend(staleService);
        }

        return true;
    }

    // Called with _stateGate held, once the activation of the new project is sure to start.
    private PreparedNewProject? TakePreparedNewProjectLocked(Project project)
    {
        if (_preparedNewProject is not { } prepared
            || !ReferenceEquals(prepared.Project, project)
            || !ReferenceEquals(prepared.Transition, _projectService.CurrentTransition))
        {
            return null;
        }

        _preparedNewProject = null;
        return prepared;
    }

    private void ReleasePreparedNewProject(NewProjectSetup setup)
    {
        IProjectVersionControlBackend? discarded = null;
        lock (_stateGate)
        {
            if (_preparedNewProject is { } prepared && ReferenceEquals(prepared.Setup, setup))
            {
                _preparedNewProject = null;
                discarded = prepared.Service;
            }
        }

        if (discarded is not null)
        {
            DiscardNewProjectBackend(discarded);
        }
    }

    private void DiscardNewProjectBackend(IProjectVersionControlBackend service)
    {
        RetireService(new ServiceRetirement(service, Task.CompletedTask));
    }

    private async Task UntrackReservedPathsIfConfirmedAsync(
        IProjectVersionControlBackend service,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> reservedPaths = await service.GetTrackedReservedPathsAsync(
            cancellationToken);
        if (reservedPaths.Count > 0
            && await ConfirmUntrackReservedPathsAsync(reservedPaths, cancellationToken))
        {
            await service.UntrackReservedPathsAsync(reservedPaths, cancellationToken);
        }
    }

    private async Task<bool> InitializeWithEditorSuspensionAsync(
        IProjectVersionControlBackend service,
        InitOptions options,
        Project expectedProject,
        CancellationToken cancellationToken)
    {
        IDisposable? editorSuspension = null;
        try
        {
            // The editor area shows the initialization for exactly as long as the editors are suspended,
            // so it is also hidden whenever the caller releases them to ask for an identity.
            editorSuspension = await SuspendEditorsBehindActivityAsync(
                ProjectLifecycleActivity.EnablingVersionControl,
                cancellationToken);

            // The initial revision has to record what the user sees, so in-memory edits reach disk
            // first. Keep the outer suspension through InitializeAsync: its Git hooks can await
            // arbitrary work, and edits made after this save would otherwise miss the commit.
            if (!await TrySaveOpenProjectAsync(expectedProject, cancellationToken))
            {
                PublishNotification(() =>
                    NotificationService.ShowWarning(
                        Strings.VersionControl,
                        MessageStrings.OperationFailed));
                return false;
            }

            await service.InitializeAsync(options, cancellationToken);
            return true;
        }
        finally
        {
            await ReleaseEditorSuspensionAsync(editorSuspension);
        }
    }

    private async Task<RepositoryInfo?> SelectRepositoryForInitializationAsync(
        IProjectVersionControlBackend service,
        string projectRoot,
        CancellationToken cancellationToken)
    {
        RepositoryInfo? discovered = await service.DiscoverRepositoryAsync(
            projectRoot,
            cancellationToken);
        if (discovered is not { IsNestedInForeignRepo: true })
        {
            return discovered ?? new RepositoryInfo(projectRoot, projectRoot);
        }

        return await ConfirmUseEnclosingRepositoryAsync(discovered, cancellationToken)
            ? discovered
            : null;
    }

    // What a creation's initialization leaves for the activation of its project: the backend that recorded
    // the first version, or, when initialization stopped after creating or attaching the repository, that
    // repository to look at again.
    private sealed record PreparedNewProject(
        NewProjectSetup Setup,
        ProjectTransitionContext Transition,
        Project Project,
        IProjectVersionControlBackend? Service,
        RepositoryInfo? InterruptedRepository);

    private sealed class NewProjectSetup(
        VersionControlCoordinator owner,
        Func<CancellationToken, Task<GitIdentity?>> requestIdentityAsync,
        IDisposable presentation) : INewProjectVersionControlSetup
    {
        private int _initializationStarted;
        private int _disposed;

        public Func<CancellationToken, Task<GitIdentity?>> RequestIdentityAsync => requestIdentityAsync;

        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public Task<bool> InitializeAsync(Project project, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(project);
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (Interlocked.Exchange(ref _initializationStarted, 1) != 0)
            {
                throw new InvalidOperationException("The new project has already been initialized.");
            }

            return owner.InitializeNewProjectAsync(this, project, cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }

            try
            {
                owner.ReleasePreparedNewProject(this);
            }
            finally
            {
                presentation.Dispose();
            }

            return ValueTask.CompletedTask;
        }
    }
}
