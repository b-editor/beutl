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

public sealed partial class ProjectService
{
    private readonly Subject<(Project? New, Project? Old)> _projectObservable = new();
    private readonly IObservable<(Project? New, Project? Old)> _safeProjectObservable;
    private readonly ReadOnlyReactivePropertySlim<bool> _isOpened;
    private readonly BeutlApplication _app = BeutlApplication.Current;
    private readonly ILogger _logger = Log.CreateLogger<ProjectService>();

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

    /// <summary>
    /// Raised before <see cref="Closing"/>, while the editors are still open. Anything that has to
    /// read or persist live editor state has to run here: <see cref="Closing"/> is where the
    /// editor host clears and disposes the tabs.
    /// </summary>
    internal event Func<ProjectCloseContext, CancellationToken, Task>? ClosingPreparing;

    internal event Func<ProjectCloseContext, CancellationToken, Task>? Closing;

    internal event Func<ProjectCloseContext, CancellationToken, Task>? ClosingFinalizing;

    internal event Func<ProjectOpenAttempt, CancellationToken, Task<ProjectOpenPreparation?>>?
        OpeningPreflight;

    internal event Func<string, Task>? Opening;

    internal event Func<Project, Task>? Opened;

    internal event Action<Project?>? TransitionCommitted;

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
            finally
            {
                EndOpenPreflight();
            }

            // A missing file is worth a transition only when a preparation can bring it back: an
            // interrupted pull leaves it missing and its recovery is one of these preparations.
            // Deciding before the transition also keeps a plainly deleted project from taking one.
            if (preparations.Count == 0 && !File.Exists(file))
            {
                _logger.LogInformation(
                    "Skipping project open: file is unavailable. File: {File}",
                    file);
                NotificationService.ShowInformation(Strings.File, MessageStrings.FileDoesNotExist);
                return;
            }

            ProjectTransitionScope transition;
            try
            {
                transition = await BeginTransitionAsync(
                    ProjectTransitionPurpose.Normal,
                    attempt,
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

    public void CloseProject()
    {
        TryCloseProject();
    }

    internal bool TryCloseProject()
    {
        try
        {
            CloseProjectOrThrow();
            return true;
        }
        catch (ProjectCloseAbortedException)
        {
            return false;
        }
    }

    internal void CloseProjectOrThrow()
    {
        Task close = CloseProjectAsync();
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            while (!close.IsCompleted)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Thread.Sleep(1);
            }
        }

        close.GetAwaiter().GetResult();
    }

    internal Task CloseProjectAsync(CancellationToken cancellationToken = default)
        => CloseProjectWithCallbackAsync(null, cancellationToken);

    internal Task CloseProjectForUpdateAsync(Func<Task<bool>> beforeCommit)
        => CloseProjectWithCallbackAsync(beforeCommit, CancellationToken.None);

    private async Task CloseProjectWithCallbackAsync(Func<Task<bool>>? beforeCommit, CancellationToken cancellationToken)
    {
        await using ProjectTransitionScope transition = await BeginTransitionAsync(
            ProjectTransitionPurpose.Normal,
            this,
            cancellationToken);
        await CloseProjectCoreAsync(transition.Context, cancellationToken, beforeCommit: beforeCommit);
    }

    internal async Task<bool> TryCloseProjectAsync(
        Project expectedProject,
        ProjectCloseIntent closeIntent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedProject);
        try
        {
            await using ProjectTransitionScope transition = await BeginTransitionAsync(
                ProjectTransitionPurpose.Normal,
                this,
                cancellationToken);
            if (!ReferenceEquals(_app.Project, expectedProject))
            {
                return false;
            }

            await CloseProjectCoreAsync(transition.Context, cancellationToken, closeIntent);
            return true;
        }
        catch (ProjectCloseAbortedException)
        {
            return false;
        }
    }

    public Task<Project?> CreateProject(int width, int height, int framerate, int samplerate, string name, string location)
    {
        return CreateProject(width, height, framerate, samplerate, name, location, beforeOpening: null);
    }

    // beforeOpening runs once the project files are written and before the project opens, inside the same
    // transition, so the editor opens only after it completes. Its token is canceled when another project
    // transition is requested meanwhile; the project then opens without waiting for the rest of the step.
    internal async Task<Project?> CreateProject(
        int width,
        int height,
        int framerate,
        int samplerate,
        string name,
        string location,
        Func<Project, CancellationToken, Task>? beforeOpening)
    {
        var creation = new ProjectCreation();
        await using ProjectTransitionScope transition = await BeginTransitionAsync(
            ProjectTransitionPurpose.Normal,
            creation,
            CancellationToken.None);
        return await CreateProjectCoreAsync(
            width,
            height,
            framerate,
            samplerate,
            name,
            location,
            beforeOpening,
            creation.PreparationCancellation,
            transition.Context);
    }

    private async Task OpenProjectCoreAsync(string file, ProjectTransitionContext transition)
    {
        VerifyTransition(transition);
        await App.WaitLoadingExtensions();

        using Activity? activity = Telemetry.StartActivity();
        using UsageTelemetry.Operation? usage = UsageTelemetry.Current?.Begin("project.open");
        Project? previousProject = null;
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
                usage?.Complete("skipped");
                return;
            }

            // Selecting the active project from Recents must not replace in-memory edits
            // with a snapshot read before the close path has saved them.
            if (_app.Project?.Uri is { IsFile: true } currentUri
                && FilePathComparison.AreSameCanonicalPath(currentUri.LocalPath, file))
            {
                TryAddToRecentProjects(file);
                usage?.Complete("skipped");
                return;
            }

            (NuGetVersion appVersion, NuGetVersion minVersion) = await GetProjectVersion(file);
            activity?.SetTag(nameof(appVersion), appVersion.ToString());
            activity?.SetTag(nameof(minVersion), minVersion.ToString());
            if (minVersion > NuGetVersion.Parse(BeutlApplication.Version) &&
                !Preferences.Default.Get("ProjectService.SkipVersionCheck", false))
            {
                var dialog = new FAContentDialog
                {
                    Title = MessageStrings.ProjectVersionMismatch_Title,
                    Content = string.Format(MessageStrings.ProjectVersionMismatch_Content, minVersion),
                    PrimaryButtonText = Strings.Close
                };
                await dialog.ShowAsync();
                usage?.Complete("skipped");
                return;
            }

            Uri projectUri = UriHelper.CreateFromPath(file);
            previousProject = _app.Project;
            if (previousProject is not null)
            {
                // Validate before closing so an invalid project leaves the current one open.
                // Closing can save scenes or sidecars shared with this project, so discard
                // the preflight graph and load the committed files after the close completes.
                _ = CoreSerializer.RestoreFromUri<Project>(projectUri);
                await CloseProjectCoreAsync(transition, CancellationToken.None);
            }

            var project = CoreSerializer.RestoreFromUri<Project>(projectUri);
            await ActivateProjectAsync(project);

            TryAddToRecentProjects(file);
            _logger.LogInformation("Opened project. File: {File}, AppVersion: {AppVersion}, MinVersion: {MinVersion}", file, appVersion, minVersion);
            PublishProjectChange((New: project, null));
            PublishTransitionCommitted(project);
            usage?.Complete();
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            _logger.LogError(ex, "Unable to open the project. File: {File}", file);
            if (previousProject is not null && _app.Project is null)
            {
                try
                {
                    // A fresh restore or activation can fail after the old editors have
                    // closed. Reuse their model so even unsaved edits survive the failure.
                    await ActivateProjectAsync(previousProject);
                    if (previousProject.Uri is { IsFile: true } previousUri)
                        TryAddToRecentProjects(previousUri.LocalPath);
                    PublishProjectChange((New: previousProject, null));
                    PublishTransitionCommitted(previousProject);
                }
                catch (Exception recoveryFailure)
                {
                    _logger.LogError(recoveryFailure, "Unable to restore the previous project after opening {File} failed.", file);
                }
            }
            NotificationService.ShowInformation(Strings.Project, MessageStrings.FailedToOpenProject);
        }
    }

    private async Task CloseProjectCoreAsync(
        ProjectTransitionContext transition,
        CancellationToken cancellationToken,
        ProjectCloseIntent closeIntent = ProjectCloseIntent.SaveChanges,
        Func<Task<bool>>? beforeCommit = null)
    {
        VerifyTransition(transition);
        if (_app.Project is not { } closingProject)
        {
            if (beforeCommit is not null && !await beforeCommit())
                throw new ProjectCloseAbortedException(MessageStrings.OperationFailed);
            return;
        }

        var closeContext = new ProjectCloseContext(closeIntent);
        try
        {
            await NotifyClosingPreparingAsync(closeContext, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await NotifyClosingAsync(closeContext, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await NotifyClosingFinalizingAsync(closeContext);
            // Keep the project published until the updater accepts the handoff. On
            // failure, the existing aborted-close completions restore its editor tabs.
            if (beforeCommit is not null && !await beforeCommit())
                throw new ProjectCloseAbortedException(MessageStrings.OperationFailed);
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
            PublishProjectChange((New: null, project));
            _app.Project = null;
            Media.FontManager.Instance.ClearProjectFonts(project);
            try
            {
                GlobalConfiguration.Instance.ViewConfig.LastOpenedProjectFile = null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clear the last-opened project setting.");
            }
            _logger.LogInformation("Closed project. Project: {Project}", project.Uri);
            PublishTransitionCommitted(null);
        }
    }

    private async Task<Project?> CreateProjectCoreAsync(
        int width,
        int height,
        int framerate,
        int samplerate,
        string name,
        string location,
        Func<Project, CancellationToken, Task>? beforeOpening,
        CancellationToken preparationCancellation,
        ProjectTransitionContext transition)
    {
        VerifyTransition(transition);
        await App.WaitLoadingExtensions();

        using Activity? activity = Telemetry.StartActivity();
        using UsageTelemetry.Operation? usage = UsageTelemetry.Current?.Begin("project.create");
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

            if (beforeOpening is not null)
            {
                try
                {
                    await beforeOpening(project, preparationCancellation);
                }
                catch (OperationCanceledException) when (preparationCancellation.IsCancellationRequested)
                {
                    // The request that canceled the step is waiting for this transition; the project
                    // it created still opens for that request to act on.
                }
            }

            PublishProjectChange((New: project, null));
            await ActivateProjectAsync(project);

            TryAddToRecentProjects(project.Uri.LocalPath);
            _logger.LogInformation("Created new project. Name: {Name}, Location: {Location}, Width: {Width}, Height: {Height}, Framerate: {Framerate}, Samplerate: {Samplerate}", name, location, width, height, framerate, samplerate);
            PublishTransitionCommitted(project);

            usage?.Complete();
            return project;
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException) usage?.Complete("cancelled");
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

    private async Task ActivateProjectAsync(Project project)
    {
        try
        {
            Media.FontManager.Instance.LoadProjectFonts(project);
            _app.Project = project;
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

        var closeContext = new ProjectCloseContext(ProjectCloseIntent.SaveChanges);
        try
        {
            foreach (Func<ProjectCloseContext, CancellationToken, Task> handler
                     in EnumerateRollbackCloseHandlers())
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

            await NotifyClosingFinalizingAsync(closeContext);

            if (ReferenceEquals(_app.Project, project))
            {
                _app.Project = null;
                Media.FontManager.Instance.ClearProjectFonts(project);
            }
        }
        finally
        {
            bool projectClosed = !ReferenceEquals(_app.Project, project);
            await closeContext.CompleteAsync(projectClosed, _logger);
        }
    }
}
