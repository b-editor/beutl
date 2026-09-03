using System.Reactive.Subjects;
using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Logging;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services.Tutorials;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Reactive.Bindings;

namespace Beutl.Services;

public sealed class ProjectService
{
    private readonly Subject<(Project? New, Project? Old)> _projectObservable = new();
    private readonly ReadOnlyReactivePropertySlim<bool> _isOpened;
    private readonly BeutlApplication _app = BeutlApplication.Current;
    private readonly ILogger _logger = Log.CreateLogger<ProjectService>();
    private readonly object _projectOperationGate = new();
    private Task _projectOperationTask = Task.CompletedTask;
    private bool _projectTransitionAdmissionClosed;
    private TaskCompletionSource? _projectTransitionAdmission;
    private readonly object _closeGate = new();
    private Task? _activeCloseTask;
    private Task? _activeCloseOperationTail;
    private readonly object _projectNotificationGate = new();
    private Task _projectNotificationTask = Task.CompletedTask;
    private readonly object _projectChangeGate = new();
    private IProjectChangeHandler? _projectChangeHandler;
    private IProjectChangeHandler? _closingProjectChangeHandler;
    private Task _lastProjectChangeTask = Task.CompletedTask;

    public ProjectService()
    {
        CurrentProject = _app.GetObservable(BeutlApplication.ProjectProperty)
            .ToReadOnlyReactivePropertySlim();
        _isOpened = CurrentProject.Select(v => v != null).ToReadOnlyReactivePropertySlim();
    }

    /// <summary>Gets ordered post-commit project notifications.</summary>
    /// <remarks>
    /// Notifications run after the editor reaches a stable state and are not part of the
    /// completion boundary of project operations. Observers must not synchronously block on a
    /// new project operation; start or await it after returning from the callback. Each payload is
    /// a historical transition, so <see cref="CurrentProject"/> may already reflect a later one.
    /// </remarks>
    public IObservable<(Project? New, Project? Old)> ProjectObservable => _projectObservable;

    /// <summary>Gets the project whose editor transition has completed.</summary>
    public IReadOnlyReactiveProperty<Project?> CurrentProject { get; }

    public IReadOnlyReactiveProperty<bool> IsOpened => _isOpened;

    internal Func<string, Task>? BeforeCreateProjectPreparation { get; set; }

    internal Action<string>? AfterCreateProjectPreparation { get; set; }

    internal Func<Task>? BeforeProjectChangeHandlerInitialization { get; set; }

    internal ProjectChangeRegistration RegisterProjectChangeHandler(IProjectChangeHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Task previous;
        Project? current;
        TaskCompletionSource? admission;
        var initialization = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_projectOperationGate)
        {
            if (!_projectTransitionAdmissionClosed && !_projectOperationTask.IsCompleted)
            {
                throw new InvalidOperationException(
                    "An editor host cannot be registered during a project transition.");
            }

            lock (_projectChangeGate)
            {
                if (_projectChangeHandler is not null || _closingProjectChangeHandler is not null)
                {
                    throw new InvalidOperationException(
                        "An editor host is already registered or still completing shutdown.");
                }

                if (_projectTransitionAdmissionClosed
                    && (_projectTransitionAdmission is null
                        || _projectTransitionAdmission.Task.IsCompleted))
                {
                    _projectTransitionAdmission = new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }

                _projectChangeHandler = handler;
                current = _app.Project;
                previous = _lastProjectChangeTask;
                _lastProjectChangeTask = initialization.Task;
                admission = _projectTransitionAdmissionClosed
                    ? _projectTransitionAdmission
                    : null;
            }
        }

        _ = InitializeProjectChangeHandlerAsync(
            previous,
            handler,
            current,
            initialization,
            admission);

        return new ProjectChangeRegistration(this, handler);
    }

    private async Task InitializeProjectChangeHandlerAsync(
        Task previous,
        IProjectChangeHandler handler,
        Project? current,
        TaskCompletionSource completion,
        TaskCompletionSource? admission)
    {
        try
        {
            await previous;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A previous project change failed before editor-host initialization.");
        }

        try
        {
            if (BeforeProjectChangeHandlerInitialization is { } beforeInitialization)
            {
                await beforeInitialization();
            }

            await handler.ApplyProjectChangeAsync(current, null);
            await handler.WaitForPendingProjectChangesAsync();
            OpenProjectTransitionAdmission(admission);
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            CloseProjectTransitionAdmission(admission, ex);
            completion.TrySetException(ex);
        }
    }

    private void OpenProjectTransitionAdmission(TaskCompletionSource? admission)
    {
        if (admission is null)
            return;

        lock (_projectOperationGate)
        {
            bool isCurrent = ReferenceEquals(_projectTransitionAdmission, admission);
            if (isCurrent)
                _projectTransitionAdmissionClosed = false;
            admission.TrySetResult();
        }
    }

    private void CloseProjectTransitionAdmission(
        TaskCompletionSource? admission,
        Exception exception)
    {
        if (admission is null)
            return;

        lock (_projectOperationGate)
        {
            admission.TrySetException(exception);
        }
    }

    /// <summary>
    /// Waits until every project transition accepted before this call, together with causally
    /// queued project-item changes, has reached a stable editor state.
    /// </summary>
    public async Task WaitForPendingProjectChangesAsync()
    {
        Task transition;
        IProjectChangeHandler? handler;
        Task lastPublished;
        lock (_projectOperationGate)
        {
            transition = _projectOperationTask;
        }

        lock (_projectChangeGate)
        {
            handler = _projectChangeHandler;
            lastPublished = _lastProjectChangeTask;
        }

        await transition;
        await lastPublished;
        if (handler is not null)
        {
            await handler.WaitForPendingProjectChangesAsync();
        }
    }

    private async Task PublishProjectChangeAsync(
        Project? @new,
        Project? old,
        Task operationCompletion)
    {
        Task previous;
        IProjectChangeHandler? handler;
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_projectChangeGate)
        {
            previous = _lastProjectChangeTask;
            handler = _projectChangeHandler;
            _lastProjectChangeTask = completion.Task;
        }

        _ = CompleteProjectChangeAsync(
            previous,
            handler,
            @new,
            old,
            operationCompletion,
            completion);
        await completion.Task;
    }

    private async Task CompleteProjectChangeAsync(
        Task previous,
        IProjectChangeHandler? handler,
        Project? @new,
        Project? old,
        Task operationCompletion,
        TaskCompletionSource completion)
    {
        try
        {
            await previous;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A previous project change failed before the next change could run.");
        }

        try
        {
            if (handler is not null)
            {
                await handler.ApplyProjectChangeAsync(@new, old);
                await handler.WaitForPendingProjectChangesAsync();
            }

            QueueProjectNotification(@new, old, operationCompletion);
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private void QueueProjectNotification(Project? @new, Project? old, Task operationCompletion)
    {
        Task previous;
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_projectNotificationGate)
        {
            previous = _projectNotificationTask;
            _projectNotificationTask = completion.Task;
        }

        _ = CompleteProjectNotificationAsync(previous, operationCompletion, @new, old, completion);
    }

    private async Task CompleteProjectNotificationAsync(
        Task previous,
        Task operationCompletion,
        Project? @new,
        Project? old,
        TaskCompletionSource completion)
    {
        try
        {
            await previous;
            await operationCompletion;
            _projectObservable.OnNext((@new, old));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A project observer failed after the editor reached a stable state.");
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    private async Task BeginUnregisterProjectChangeHandlerAsync(IProjectChangeHandler handler)
    {
        Task operation;
        lock (_projectOperationGate)
        {
            operation = _projectOperationTask;
            lock (_projectChangeGate)
            {
                if (!ReferenceEquals(_projectChangeHandler, handler))
                    return;
            }

            _projectTransitionAdmissionClosed = true;
            _projectTransitionAdmission = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        // Keep the handler installed while already-admitted transitions drain. An operation can
        // be queued before unregister starts but reach PublishProjectChangeAsync afterwards.
        await operation;

        Task pending;
        lock (_projectChangeGate)
        {
            if (ReferenceEquals(_projectChangeHandler, handler))
            {
                _projectChangeHandler = null;
                _closingProjectChangeHandler = handler;
            }

            pending = _lastProjectChangeTask;
        }

        try
        {
            await pending;
        }
        finally
        {
            FailProjectTransitionAdmission();
        }
    }

    private void FailProjectTransitionAdmission()
    {
        lock (_projectOperationGate)
        {
            if (_projectTransitionAdmissionClosed)
            {
                _projectTransitionAdmission?.TrySetException(new InvalidOperationException(
                    "The editor host was unregistered before the project transition could be applied."));
            }
        }
    }

    private void CompleteUnregisterProjectChangeHandler(IProjectChangeHandler handler)
    {
        lock (_projectChangeGate)
        {
            if (ReferenceEquals(_closingProjectChangeHandler, handler))
            {
                _closingProjectChangeHandler = null;
            }
        }
    }

    private Task EnqueueProjectTransitionAsync(Func<ProjectTransitionContext, Task> transition)
        => EnqueueProjectTransitionAsync(transition, out _);

    private Task EnqueueProjectTransitionAsync(
        Func<ProjectTransitionContext, Task> transition,
        out Task operationTail)
    {
        return EnqueueProjectTransitionAsync(async context =>
        {
            await transition(context);
            return true;
        }, out operationTail);
    }

    private Task<T> EnqueueProjectTransitionAsync<T>(
        Func<ProjectTransitionContext, Task<T>> transition)
        => EnqueueProjectTransitionAsync(transition, out _);

    private Task<T> EnqueueProjectTransitionAsync<T>(
        Func<ProjectTransitionContext, Task<T>> transition,
        out Task operationTail)
    {
        ArgumentNullException.ThrowIfNull(transition);
        Task previous;
        ProjectTransitionContext context;
        var result = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_projectOperationGate)
        {
            previous = _projectOperationTask;
            Task admission = _projectTransitionAdmissionClosed
                ? _projectTransitionAdmission?.Task ?? Task.CompletedTask
                : Task.CompletedTask;
            context = new ProjectTransitionContext(admission);
            _projectOperationTask = context.Completion;
            operationTail = context.Completion;
        }

        _ = CompleteProjectTransitionAsync(previous, context, transition, result);
        return result.Task;
    }

    private async Task CompleteProjectTransitionAsync<T>(
        Task previous,
        ProjectTransitionContext context,
        Func<ProjectTransitionContext, Task<T>> transition,
        TaskCompletionSource<T> result)
    {
        await previous;
        try
        {
            await context.Admission;
            T value = await transition(context);
            context.Release();
            result.TrySetResult(value);
        }
        catch (Exception ex)
        {
            context.Release();
            result.TrySetException(ex);
        }
    }

    private static async Task<(NuGetVersion AppVersion, NuGetVersion MinVersion)> GetProjectVersion(string file)
    {
        await using var stream = File.OpenRead(file);
        var node = await JsonNode.ParseAsync(stream);
        string? appVersion = (string?)node?["appVersion"];
        string? minAppVersion = (string?)node?["minAppVersion"];
        if (appVersion == null || minAppVersion == null)
        {
            throw new InvalidOperationException("The project file does not contain version information.");
        }

        return (NuGetVersion.Parse(appVersion), NuGetVersion.Parse(minAppVersion));
    }

    /// <summary>Opens a project after its editor host has accepted the serialized transition.</summary>
    /// <exception cref="InvalidOperationException">
    /// The editor host is unregistering and cannot apply the transition.
    /// </exception>
    public Task OpenProject(string file)
        => EnqueueProjectTransitionAsync(context => CompleteOpenProjectAsync(context, file));

    private static async Task<OpenProjectPreparation?> PrepareOpenProjectAsync(string file)
    {
        await App.WaitLoadingExtensions();
        (NuGetVersion appVersion, NuGetVersion minVersion) = await GetProjectVersion(file);
        if (minVersion > NuGetVersion.Parse(BeutlApplication.Version) &&
            !Preferences.Default.Get("ProjectService.SkipVersionCheck", false))
        {
            var dialog = new ContentDialog
            {
                Title = MessageStrings.ProjectVersionMismatch_Title,
                Content = string.Format(MessageStrings.ProjectVersionMismatch_Content, minVersion),
                PrimaryButtonText = Strings.Close
            };
            await dialog.ShowAsync();
            return null;
        }

        var project = CoreSerializer.RestoreFromUri<Project>(UriHelper.CreateFromPath(file));
        return new OpenProjectPreparation(project, appVersion, minVersion);
    }

    private async Task CompleteOpenProjectAsync(
        ProjectTransitionContext context,
        string file)
    {
        using Activity? activity = Telemetry.StartActivity();
        try
        {
            OpenProjectPreparation? prepared = await PrepareOpenProjectAsync(file);
            if (prepared is null)
            {
                context.Release();
                return;
            }

            activity?.SetTag(nameof(prepared.AppVersion), prepared.AppVersion.ToString());
            activity?.SetTag(nameof(prepared.MinVersion), prepared.MinVersion.ToString());
            await ReplaceProjectAsync(context, prepared.Project);

            AddToRecentProjects(file);
            _logger.LogInformation(
                "Opened project. File: {File}, AppVersion: {AppVersion}, MinVersion: {MinVersion}",
                file,
                prepared.AppVersion,
                prepared.MinVersion);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            _logger.LogError(ex, "Unable to open the project. File: {File}", file);
            NotificationService.ShowInformation(Strings.Project, MessageStrings.FailedToOpenProject);
        }
    }

    /// <summary>Closes the current project and waits for terminal editor-context teardown.</summary>
    /// <remarks>
    /// Repeated calls join the serialized transition. Do not synchronously block this task from a
    /// <see cref="CurrentProject"/> or <see cref="ProjectObservable"/> callback.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The editor host is unregistering and cannot apply the transition.
    /// </exception>
    public Task CloseProjectAsync()
    {
        lock (_closeGate)
        {
            Task currentOperation;
            lock (_projectOperationGate)
            {
                currentOperation = _projectOperationTask;
            }

            if (_activeCloseTask is { IsCompleted: false }
                && ReferenceEquals(_activeCloseOperationTail, currentOperation))
            {
                return _activeCloseTask;
            }

            _activeCloseTask = EnqueueProjectTransitionAsync(
                CloseProjectCoreAsync,
                out _activeCloseOperationTail);
            return _activeCloseTask;
        }
    }

    private async Task CloseProjectCoreAsync(ProjectTransitionContext context)
    {
        if (_app.Project is { } project)
        {
            await PublishProjectChangeAsync(null, project, context.Completion);
            GlobalConfiguration.Instance.ViewConfig.LastOpenedProjectFile = null;
            _logger.LogInformation("Closed project. Project: {Project}", project.Uri);
            CommitProjectState(context, null);
        }
        else
        {
            context.Release();
        }
    }

    /// <summary>Creates and opens a project after its editor host accepts the transition.</summary>
    /// <exception cref="InvalidOperationException">
    /// The editor host is unregistering and cannot apply the transition.
    /// </exception>
    public Task<Project?> CreateProject(
        int width,
        int height,
        int framerate,
        int samplerate,
        string name,
        string location)
        => EnqueueProjectTransitionAsync(
            context => CompleteCreateProjectAsync(
                context,
                width,
                height,
                framerate,
                samplerate,
                name,
                location));

    private async Task<CreateProjectPreparation> PrepareCreateProjectAsync(
        int width,
        int height,
        int framerate,
        int samplerate,
        string name,
        string location)
    {
        await App.WaitLoadingExtensions();
        if (BeforeCreateProjectPreparation is { } beforePreparation)
        {
            await beforePreparation(name);
        }

        string projectLocation = Path.Combine(location, name);
        var scene = new Scene(width, height, name)
        {
            Uri = UriHelper.CreateFromPath(Path.Combine(
                projectLocation,
                name,
                $"{name}.{EditorConstants.SceneFileExtension}")),
        };
        var project = new Project()
        {
            Items = { scene },
            Uri = UriHelper.CreateFromPath(Path.Combine(
                projectLocation,
                $"{name}.{EditorConstants.ProjectFileExtension}")),
            Variables =
            {
                [ProjectVariableKeys.FrameRate] = framerate.ToString(),
                [ProjectVariableKeys.SampleRate] = samplerate.ToString(),
            }
        };

        CoreSerializer.StoreToUri(scene, scene.Uri);
        ProjectPersistence.PersistOrRollback(
            () => CoreSerializer.StoreToUri(project, project.Uri),
            () => DeletePreparedScene(scene));
        AfterCreateProjectPreparation?.Invoke(name);
        return new CreateProjectPreparation(project, projectLocation);
    }

    private async Task<Project?> CompleteCreateProjectAsync(
        ProjectTransitionContext context,
        int width,
        int height,
        int framerate,
        int samplerate,
        string name,
        string location)
    {
        using Activity? activity = Telemetry.StartActivity();
        activity?.SetTag(nameof(width), width);
        activity?.SetTag(nameof(height), height);
        activity?.SetTag(nameof(framerate), framerate);
        activity?.SetTag(nameof(samplerate), samplerate);
        CreateProjectPreparation? prepared = null;
        try
        {
            prepared = await PrepareCreateProjectAsync(
                width,
                height,
                framerate,
                samplerate,
                name,
                location);
            await ReplaceProjectAsync(context, prepared.Project);

            AddToRecentProjects(prepared.Project.Uri!.LocalPath);
            _logger.LogInformation("Created new project. Name: {Name}, Location: {Location}, Width: {Width}, Height: {Height}, Framerate: {Framerate}, Samplerate: {Samplerate}", name, prepared.Location, width, height, framerate, samplerate);
            return prepared.Project;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            _logger.LogError(
                ex,
                "Unable to create the project. Name: {Name}, Location: {Location}",
                name,
                prepared?.Location);
            NotificationService.ShowError(Strings.Error, ex.Message);
            return null;
        }
    }

    private void DeletePreparedScene(Scene scene)
    {
        try
        {
            File.Delete(scene.Uri!.LocalPath);
        }
        catch (Exception deleteEx)
        {
            _logger.LogWarning(deleteEx, "Failed to delete orphaned scene file: {Uri}", scene.Uri);
        }
    }

    private async Task ReplaceProjectAsync(ProjectTransitionContext context, Project project)
    {
        Project? old = _app.Project;
        if (ReferenceEquals(old, project))
        {
            context.Release();
            return;
        }

        await PublishProjectChangeAsync(project, old, context.Completion);
        CommitProjectState(context, project);
    }

    private void CommitProjectState(ProjectTransitionContext context, Project? project)
    {
        try
        {
            _app.Project = project;
        }
        catch (Exception ex) when (ReferenceEquals(_app.Project, project))
        {
            _logger.LogError(ex, "A project observer failed after the project state was committed.");
        }
        finally
        {
            context.Release();
        }
    }

    private static void AddToRecentProjects(string file)
    {
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        viewConfig.UpdateRecentProject(file);
        viewConfig.UpdateRecentFile(file);
        viewConfig.LastOpenedProjectFile = file;
    }

    private sealed record OpenProjectPreparation(
        Project Project,
        NuGetVersion AppVersion,
        NuGetVersion MinVersion);

    private sealed record CreateProjectPreparation(
        Project Project,
        string Location);

    internal sealed class ProjectChangeRegistration(
        ProjectService owner,
        IProjectChangeHandler handler) : IAsyncDisposable
    {
        private readonly object _gate = new();
        private ProjectService? _owner = owner;
        private Task? _beginDisposeTask;

        internal Task BeginDisposeAsync()
        {
            lock (_gate)
            {
                return _beginDisposeTask ??= _owner is { } currentOwner
                    ? currentOwner.BeginUnregisterProjectChangeHandlerAsync(handler)
                    : Task.CompletedTask;
            }
        }

        internal void CompleteDispose()
        {
            ProjectService? currentOwner;
            lock (_gate)
            {
                currentOwner = _owner;
                _owner = null;
            }

            currentOwner?.CompleteUnregisterProjectChangeHandler(handler);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await BeginDisposeAsync();
            }
            finally
            {
                CompleteDispose();
            }
        }
    }

    private sealed class ProjectTransitionContext
    {
        private readonly TaskCompletionSource _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ProjectTransitionContext(Task admission)
        {
            Admission = admission;
        }

        public Task Admission { get; }

        public Task Completion => _completion.Task;

        public void Release()
            => _completion.TrySetResult();
    }
}

internal interface IProjectChangeHandler
{
    Task ApplyProjectChangeAsync(Project? @new, Project? old);

    Task WaitForPendingProjectChangesAsync();
}
