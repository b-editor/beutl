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
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly object _transitionSync = new();
    private ProjectTransitionContext? _currentTransition;
    private long _nextTransitionId;
    private int _shutdownRequested;

    public ProjectService()
    {
        CurrentProject = _app.GetObservable(BeutlApplication.ProjectProperty)
            .ToReadOnlyReactivePropertySlim();
        _isOpened = CurrentProject.Select(v => v != null).ToReadOnlyReactivePropertySlim();
    }

    public IObservable<(Project? New, Project? Old)> ProjectObservable => _projectObservable;

    internal event Func<CancellationToken, Task>? Closing;

    internal event Func<string, Task>? Opening;

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
        await using ProjectTransitionScope transition = await BeginTransitionAsync(
            ProjectTransitionPurpose.Normal,
            this,
            allowDuringShutdown: false,
            CancellationToken.None);
        await OpenProjectCoreAsync(file, transition.Context);
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
    }

    private async ValueTask<ProjectTransitionScope> BeginTransitionAsync(
        ProjectTransitionPurpose purpose,
        object owner,
        bool allowDuringShutdown,
        CancellationToken cancellationToken)
    {
        if (!allowDuringShutdown && Volatile.Read(ref _shutdownRequested) != 0)
        {
            throw new InvalidOperationException(
                "Project transitions cannot start after application shutdown has begun.");
        }

        await _transitionGate.WaitAsync(cancellationToken);
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

            _app.Project = project;
            // 値を発行
            _projectObservable.OnNext((New: project, null));

            AddToRecentProjects(file);
            _logger.LogInformation("Opened project. File: {File}, AppVersion: {AppVersion}, MinVersion: {MinVersion}", file, appVersion, minVersion);
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
        if (_app.Project is null)
        {
            return;
        }

        if (Closing is { } closing)
        {
            foreach (Func<CancellationToken, Task> handler in closing.GetInvocationList())
            {
                await handler(cancellationToken);
            }
        }

        CloseProjectImmediately();
    }

    internal void CloseProjectImmediately()
    {
        if (_app.Project is { } project)
        {
            // 値を発行
            _projectObservable.OnNext((New: null, project));
            _app.Project = null;
            GlobalConfiguration.Instance.ViewConfig.LastOpenedProjectFile = null;
            _logger.LogInformation("Closed project. Project: {Project}", project.Uri);
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

            // 値を発行
            _projectObservable.OnNext((New: project, null));
            _app.Project = project;

            AddToRecentProjects(project.Uri.LocalPath);
            _logger.LogInformation("Created new project. Name: {Name}, Location: {Location}, Width: {Width}, Height: {Height}, Framerate: {Framerate}, Samplerate: {Samplerate}", name, location, width, height, framerate, samplerate);

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

    private static void AddToRecentProjects(string file)
    {
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        viewConfig.UpdateRecentProject(file);
        viewConfig.UpdateRecentFile(file);
        viewConfig.LastOpenedProjectFile = file;
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
