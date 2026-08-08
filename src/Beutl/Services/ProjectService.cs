using System.Reactive.Linq;
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
    private readonly IObservable<(Project? New, Project? Old)> _safeProjectObservable;
    private readonly ReadOnlyReactivePropertySlim<bool> _isOpened;
    private readonly BeutlApplication _app = BeutlApplication.Current;
    private readonly ILogger _logger = Log.CreateLogger<ProjectService>();
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly object _openAttemptSync = new();
    private readonly object _transitionSync = new();
    private ProjectOpenAttempt? _currentOpenAttempt;
    private ProjectTransitionContext? _currentTransition;
    private long _nextOpenAttemptId;
    private long _nextTransitionId;
    private int _shutdownRequested;

    public ProjectService()
    {
        _safeProjectObservable = Observable.Create<(Project? New, Project? Old)>(observer =>
            _projectObservable.Subscribe(change =>
            {
                try
                {
                    observer.OnNext(change);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "A project-state observer failed while publishing the committed transition.");
                }
            }));
        CurrentProject = _app.GetObservable(BeutlApplication.ProjectProperty)
            .ToReadOnlyReactivePropertySlim();
        _isOpened = CurrentProject.Select(v => v != null).ToReadOnlyReactivePropertySlim();
    }

    public IObservable<(Project? New, Project? Old)> ProjectObservable => _safeProjectObservable;

    internal event Func<ProjectCloseContext, CancellationToken, Task>? Closing;

    internal event Func<ProjectCloseContext, CancellationToken, Task>? ClosingFinalizing;

    internal event Func<ProjectOpenAttempt, CancellationToken, Task<ProjectOpenPreparation?>>?
        OpeningPreflight;

    internal event Func<string, Task>? Opening;

    internal event Func<Project, Task>? Opened;

    internal ProjectTransitionContext? CurrentTransition
    {
        get
        {
            lock (_transitionSync)
            {
                return _currentTransition;
            }
        }
    }

    public IReadOnlyReactiveProperty<Project?> CurrentProject { get; }

    public IReadOnlyReactiveProperty<bool> IsOpened => _isOpened;

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

    public async Task OpenProject(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ProjectOpenAttempt attempt = BeginOpenAttempt(file);
        try
        {
            try
            {
                await WaitForExistingTransitionAsync(attempt);
            }
            catch (OperationCanceledException) when (attempt.IsCancellationRequested)
            {
                return;
            }

            IReadOnlyList<ProjectOpenPreparation> preparations;
            try
            {
                preparations = await NotifyOpeningPreflightAsync(attempt);
            }
            catch (OperationCanceledException) when (attempt.IsCancellationRequested)
            {
                return;
            }

            ProjectTransitionScope transition;
            try
            {
                transition = await BeginTransitionAsync(
                    ProjectTransitionPurpose.Normal,
                    attempt,
                    allowDuringShutdown: false,
                    attempt.CancellationToken);
            }
            catch (OperationCanceledException) when (attempt.IsCancellationRequested)
            {
                return;
            }

            await using (transition)
            {
                if (!attempt.TryBeginApply())
                {
                    return;
                }

                foreach (ProjectOpenPreparation preparation in preparations)
                {
                    ProjectOpenPreparationResult result = await preparation.ApplyAsync(
                        transition.Context,
                        CancellationToken.None);
                    if (result == ProjectOpenPreparationResult.Abort)
                    {
                        return;
                    }
                }

                await OpenProjectCoreAsync(file, transition.Context);
            }
        }
        finally
        {
            CompleteOpenAttempt(attempt);
        }
    }

    private async Task WaitForExistingTransitionAsync(ProjectOpenAttempt attempt)
    {
        await _transitionGate.WaitAsync(attempt.CancellationToken);
        try
        {
            attempt.CancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    public async Task CloseProject(CancellationToken cancellationToken = default)
    {
        await using ProjectTransitionScope transition = await BeginTransitionAsync(
            ProjectTransitionPurpose.Normal,
            this,
            allowDuringShutdown: false,
            cancellationToken);
        await CloseProjectCoreAsync(transition.Context, cancellationToken);
    }

    public async Task<Project?> CreateProject(int width, int height, int framerate, int samplerate, string name, string location)
    {
        await using ProjectTransitionScope transition = await BeginTransitionAsync(
            ProjectTransitionPurpose.Normal,
            this,
            allowDuringShutdown: false,
            CancellationToken.None);
        return await CreateProjectCoreAsync(
            width,
            height,
            framerate,
            samplerate,
            name,
            location,
            transition.Context);
    }

    internal ValueTask<ProjectTransitionScope> BeginVersionControlTransitionAsync(
        object owner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return BeginTransitionAsync(
            ProjectTransitionPurpose.VersionControlMutation,
            owner,
            allowDuringShutdown: false,
            cancellationToken);
    }

    internal ValueTask<ProjectTransitionScope> BeginShutdownTransitionAsync(
        object owner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Interlocked.Exchange(ref _shutdownRequested, 1);
        return BeginTransitionAsync(
            ProjectTransitionPurpose.Shutdown,
            owner,
            allowDuringShutdown: true,
            cancellationToken);
    }

    internal void RequestShutdown()
    {
        Interlocked.Exchange(ref _shutdownRequested, 1);
        CancelPendingOpenAttemptExcept(owner: null);
    }

    private async ValueTask<ProjectTransitionScope> BeginTransitionAsync(
        ProjectTransitionPurpose purpose,
        object owner,
        bool allowDuringShutdown,
        CancellationToken cancellationToken)
    {
        CancelPendingOpenAttemptExcept(owner);
        if (!allowDuringShutdown && Volatile.Read(ref _shutdownRequested) != 0)
        {
            throw new InvalidOperationException(
                "Project transitions cannot start after application shutdown has begun.");
        }

        await _transitionGate.WaitAsync(cancellationToken);
        CancelPendingOpenAttemptExcept(owner);
        if (cancellationToken.IsCancellationRequested)
        {
            _transitionGate.Release();
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (!allowDuringShutdown && Volatile.Read(ref _shutdownRequested) != 0)
        {
            _transitionGate.Release();
            throw new InvalidOperationException(
                "Project transitions cannot start after application shutdown has begun.");
        }

        var context = new ProjectTransitionContext(
            Interlocked.Increment(ref _nextTransitionId),
            purpose,
            owner);
        lock (_transitionSync)
        {
            _currentTransition = context;
        }

        return new ProjectTransitionScope(this, context);
    }

    private ProjectOpenAttempt BeginOpenAttempt(string file)
    {
        var attempt = new ProjectOpenAttempt(
            Interlocked.Increment(ref _nextOpenAttemptId),
            file);
        ProjectOpenAttempt? previous;
        lock (_openAttemptSync)
        {
            previous = _currentOpenAttempt;
            _currentOpenAttempt = attempt;
        }

        CancelOpenAttempt(previous);
        return attempt;
    }

    private async Task<IReadOnlyList<ProjectOpenPreparation>> NotifyOpeningPreflightAsync(
        ProjectOpenAttempt attempt)
    {
        if (OpeningPreflight is not { } openingPreflight)
        {
            return [];
        }

        var preparations = new List<ProjectOpenPreparation>();
        foreach (Func<ProjectOpenAttempt, CancellationToken, Task<ProjectOpenPreparation?>> handler
                 in openingPreflight.GetInvocationList())
        {
            attempt.CancellationToken.ThrowIfCancellationRequested();
            ProjectOpenPreparation? preparation = await handler(
                attempt,
                attempt.CancellationToken);
            if (preparation is not null)
            {
                preparations.Add(preparation);
            }
        }

        return preparations;
    }

    private void CancelPendingOpenAttemptExcept(object? owner)
    {
        ProjectOpenAttempt? attempt;
        lock (_openAttemptSync)
        {
            attempt = _currentOpenAttempt;
        }

        if (owner is ProjectOpenAttempt openingAttempt)
        {
            if (!ReferenceEquals(attempt, openingAttempt))
            {
                CancelOpenAttempt(openingAttempt);
            }

            return;
        }

        if (!ReferenceEquals(attempt, owner))
        {
            CancelOpenAttempt(attempt);
        }
    }

    private void CancelOpenAttempt(ProjectOpenAttempt? attempt)
    {
        try
        {
            attempt?.CancelIfPending();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A project-open cancellation callback failed.");
        }
    }

    private void CompleteOpenAttempt(ProjectOpenAttempt attempt)
    {
        lock (_openAttemptSync)
        {
            if (ReferenceEquals(_currentOpenAttempt, attempt))
            {
                _currentOpenAttempt = null;
            }
        }

        attempt.Complete();
    }

    private async Task OpenProjectCoreAsync(string file, ProjectTransitionContext transition)
    {
        VerifyTransition(transition);
        await App.WaitLoadingExtensions();

        using Activity? activity = Telemetry.StartActivity();
        try
        {
            if (Opening is { } opening)
            {
                foreach (Func<string, Task> handler in opening.GetInvocationList())
                {
                    await handler(file);
                }
            }

            if (!File.Exists(file))
            {
                _logger.LogInformation("Skipping project open: file is unavailable. File: {File}", file);
                NotificationService.ShowInformation(Strings.File, MessageStrings.FileDoesNotExist);
                return;
            }

            await CloseProjectCoreAsync(transition, CancellationToken.None);

            (NuGetVersion appVersion, NuGetVersion minVersion) = await GetProjectVersion(file);
            activity?.SetTag(nameof(appVersion), appVersion.ToString());
            activity?.SetTag(nameof(minVersion), minVersion.ToString());
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
                return;
            }

            var project = CoreSerializer.RestoreFromUri<Project>(UriHelper.CreateFromPath(file));

            await ActivateProjectAsync(project);

            TryAddToRecentProjects(file);
            _logger.LogInformation("Opened project. File: {File}, AppVersion: {AppVersion}, MinVersion: {MinVersion}", file, appVersion, minVersion);
            PublishProjectChange((New: project, null));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            _logger.LogError(ex, "Unable to open the project. File: {File}", file);
            NotificationService.ShowInformation(Strings.Project, MessageStrings.FailedToOpenProject);
        }
    }

    private async Task CloseProjectCoreAsync(
        ProjectTransitionContext transition,
        CancellationToken cancellationToken)
    {
        VerifyTransition(transition);
        if (_app.Project is not { } closingProject)
        {
            return;
        }

        var closeContext = new ProjectCloseContext();
        try
        {
            await NotifyClosingAsync(closeContext, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await NotifyClosingFinalizingAsync(closeContext);
            CloseProjectImmediately();
        }
        finally
        {
            bool projectClosed = !ReferenceEquals(_app.Project, closingProject);
            await closeContext.CompleteAsync(projectClosed, _logger);
        }
    }

    internal void CloseProjectImmediately()
    {
        if (_app.Project is { } project)
        {
            _app.Project = null;
            try
            {
                GlobalConfiguration.Instance.ViewConfig.LastOpenedProjectFile = null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clear the last-opened project setting.");
            }
            _logger.LogInformation("Closed project. Project: {Project}", project.Uri);
            PublishProjectChange((New: null, project));
        }
    }

    private async Task<Project?> CreateProjectCoreAsync(
        int width,
        int height,
        int framerate,
        int samplerate,
        string name,
        string location,
        ProjectTransitionContext transition)
    {
        VerifyTransition(transition);
        await App.WaitLoadingExtensions();

        using Activity? activity = Telemetry.StartActivity();
        activity?.SetTag(nameof(width), width);
        activity?.SetTag(nameof(height), height);
        activity?.SetTag(nameof(framerate), framerate);
        activity?.SetTag(nameof(samplerate), samplerate);
        try
        {
            await CloseProjectCoreAsync(transition, CancellationToken.None);

            location = Path.Combine(location, name);
            var scene = new Scene(width, height, name)
            {
                Uri = UriHelper.CreateFromPath(Path.Combine(location, name, $"{name}.{EditorConstants.SceneFileExtension}")),
            };
            var project = new Project()
            {
                Items = { scene },
                Uri = UriHelper.CreateFromPath(Path.Combine(location, $"{name}.{EditorConstants.ProjectFileExtension}")),
                Variables =
                {
                    [ProjectVariableKeys.FrameRate] = framerate.ToString(),
                    [ProjectVariableKeys.SampleRate] = samplerate.ToString(),
                }
            };

            CoreSerializer.StoreToUri(scene, scene.Uri);
            ProjectPersistence.PersistOrRollback(
                () => CoreSerializer.StoreToUri(project, project.Uri),
                () =>
                {
                    // The project write failed, so the scene file just saved is orphaned. Delete it
                    // (best-effort); any directories created are left in place.
                    try
                    {
                        File.Delete(scene.Uri.LocalPath);
                    }
                    catch (Exception deleteEx)
                    {
                        _logger.LogWarning(deleteEx, "Failed to delete orphaned scene file: {Uri}", scene.Uri);
                    }
                });

            await ActivateProjectAsync(project);

            TryAddToRecentProjects(project.Uri.LocalPath);
            _logger.LogInformation("Created new project. Name: {Name}, Location: {Location}, Width: {Width}, Height: {Height}, Framerate: {Framerate}, Samplerate: {Samplerate}", name, location, width, height, framerate, samplerate);
            PublishProjectChange((New: project, null));

            return project;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            _logger.LogError(ex, "Unable to create the project. Name: {Name}, Location: {Location}", name, location);
            // Surface the actual failure (disk full, permission denied, ...) instead of a generic message.
            NotificationService.ShowError(Strings.Error, ex.Message);
            return null;
        }
    }

    private void TryAddToRecentProjects(string file)
    {
        try
        {
            ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
            viewConfig.UpdateRecentProject(file);
            viewConfig.UpdateRecentFile(file);
            viewConfig.LastOpenedProjectFile = file;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update recent-project settings. File: {File}", file);
        }
    }

    private async Task NotifyOpenedAsync(Project project)
    {
        if (Opened is { } opened)
        {
            foreach (Func<Project, Task> handler in opened.GetInvocationList())
            {
                await handler(project);
            }
        }
    }

    private async Task ActivateProjectAsync(Project project)
    {
        _app.Project = project;
        try
        {
            await NotifyOpenedAsync(project);
        }
        catch
        {
            await RollBackFailedActivationAsync(project);
            throw;
        }
    }

    private async Task RollBackFailedActivationAsync(Project project)
    {
        if (!ReferenceEquals(_app.Project, project))
        {
            return;
        }

        var closeContext = new ProjectCloseContext();
        try
        {
            if (Closing is { } closing)
            {
                foreach (Func<ProjectCloseContext, CancellationToken, Task> handler
                         in closing.GetInvocationList())
                {
                    try
                    {
                        await handler(closeContext, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "A project-closing handler failed while rolling back project activation.");
                    }
                }
            }

            await NotifyClosingFinalizingAsync(closeContext);

            if (ReferenceEquals(_app.Project, project))
            {
                _app.Project = null;
            }
        }
        finally
        {
            bool projectClosed = !ReferenceEquals(_app.Project, project);
            await closeContext.CompleteAsync(projectClosed, _logger);
        }
    }

    private async Task NotifyClosingAsync(
        ProjectCloseContext closeContext,
        CancellationToken cancellationToken)
    {
        if (Closing is { } closing)
        {
            foreach (Func<ProjectCloseContext, CancellationToken, Task> handler
                     in closing.GetInvocationList())
            {
                await handler(closeContext, cancellationToken);
            }
        }
    }

    private async Task NotifyClosingFinalizingAsync(ProjectCloseContext closeContext)
    {
        if (ClosingFinalizing is { } closingFinalizing)
        {
            foreach (Func<ProjectCloseContext, CancellationToken, Task> handler
                     in closingFinalizing.GetInvocationList())
            {
                try
                {
                    await handler(closeContext, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "A project-close finalizer failed.");
                }
            }
        }
    }

    private void PublishProjectChange((Project? New, Project? Old) change)
    {
        try
        {
            _projectObservable.OnNext(change);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unable to publish a committed project-state transition.");
        }
    }

    private void VerifyTransition(ProjectTransitionContext transition)
    {
        lock (_transitionSync)
        {
            if (!ReferenceEquals(_currentTransition, transition))
            {
                throw new InvalidOperationException("The project transition is no longer active.");
            }
        }
    }

    private void EndTransition(ProjectTransitionContext transition)
    {
        lock (_transitionSync)
        {
            if (!ReferenceEquals(_currentTransition, transition))
            {
                return;
            }

            _currentTransition = null;
        }

        _transitionGate.Release();
    }

    internal sealed class ProjectCloseContext
    {
        private readonly object _gate = new();
        private readonly List<Func<bool, Task>> _completions = [];
        private bool _completed;

        internal void RegisterCompletion(Func<bool, Task> completion)
        {
            ArgumentNullException.ThrowIfNull(completion);
            lock (_gate)
            {
                if (_completed)
                {
                    throw new InvalidOperationException(
                        "The project-close transition has already completed.");
                }

                _completions.Add(completion);
            }
        }

        internal async Task CompleteAsync(bool projectClosed, ILogger logger)
        {
            Func<bool, Task>[] completions;
            lock (_gate)
            {
                if (_completed)
                {
                    return;
                }

                _completed = true;
                completions = _completions.ToArray();
                _completions.Clear();
            }

            foreach (Func<bool, Task> completion in completions)
            {
                try
                {
                    await completion(projectClosed);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "A project-close completion callback failed.");
                }
            }
        }
    }

    internal sealed class ProjectOpenAttempt
    {
        private readonly CancellationTokenSource _cancellation = new();
        private readonly object _gate = new();
        private ProjectOpenAttemptState _state;

        internal ProjectOpenAttempt(long id, string projectFile)
        {
            Id = id;
            ProjectFile = projectFile;
        }

        internal long Id { get; }

        internal string ProjectFile { get; }

        internal CancellationToken CancellationToken => _cancellation.Token;

        internal bool IsCancellationRequested => _cancellation.IsCancellationRequested;

        internal bool TryBeginApply()
        {
            lock (_gate)
            {
                if (_state != ProjectOpenAttemptState.Pending
                    || _cancellation.IsCancellationRequested)
                {
                    return false;
                }

                _state = ProjectOpenAttemptState.Applying;
                return true;
            }
        }

        internal void CancelIfPending()
        {
            bool cancel;
            lock (_gate)
            {
                cancel = _state == ProjectOpenAttemptState.Pending;
                if (cancel)
                {
                    _state = ProjectOpenAttemptState.Cancelled;
                }
            }

            if (cancel)
            {
                _cancellation.Cancel();
            }
        }

        internal void Complete()
        {
            lock (_gate)
            {
                _state = ProjectOpenAttemptState.Completed;
            }

            _cancellation.Dispose();
        }
    }

    internal abstract class ProjectOpenPreparation
    {
        internal abstract Task<ProjectOpenPreparationResult> ApplyAsync(
            ProjectTransitionContext transition,
            CancellationToken cancellationToken);
    }

    internal sealed class ProjectTransitionScope : IAsyncDisposable
    {
        private ProjectService? _owner;

        internal ProjectTransitionScope(ProjectService owner, ProjectTransitionContext context)
        {
            _owner = owner;
            Context = context;
        }

        internal ProjectTransitionContext Context { get; }

        internal Task CloseProjectAsync(CancellationToken cancellationToken = default)
        {
            ProjectService owner = _owner
                                   ?? throw new ObjectDisposedException(nameof(ProjectTransitionScope));
            return owner.CloseProjectCoreAsync(Context, cancellationToken);
        }

        internal Task OpenProjectAsync(string file)
        {
            ProjectService owner = _owner
                                   ?? throw new ObjectDisposedException(nameof(ProjectTransitionScope));
            return owner.OpenProjectCoreAsync(file, Context);
        }

        public ValueTask DisposeAsync()
        {
            ProjectService? owner = Interlocked.Exchange(ref _owner, null);
            owner?.EndTransition(Context);
            return ValueTask.CompletedTask;
        }
    }

    private enum ProjectOpenAttemptState
    {
        Pending,
        Applying,
        Cancelled,
        Completed,
    }
}

internal enum ProjectOpenPreparationResult
{
    Proceed,
    Abort,
}

internal enum ProjectTransitionPurpose
{
    Normal,
    VersionControlMutation,
    Shutdown,
}

internal sealed record ProjectTransitionContext(
    long Id,
    ProjectTransitionPurpose Purpose,
    object Owner);
