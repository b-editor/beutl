using System.Reactive.Linq;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Logging;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Services.Tutorials;
using Beutl.Utilities;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Services;

public sealed partial class ProjectService
{
    private readonly INotificationService _notifications;
    private readonly EditorService? _editorService;
    internal INotificationService Notifications => _notifications;

    private readonly ReadOnlyReactivePropertySlim<bool> _isOpened;
    private readonly ILogger _logger = Log.CreateLogger<ProjectService>();
    // Dismisses the notices about the open project once it closes.
    private CancellationTokenSource? _projectNoticeDismissal;

    private readonly ViewConfig _configuration;

    // The host's fonts, which every project's fonts fork.
    public Media.FontManager Fonts { get; }

    // The open project's fork of Fonts, which holds its bundled fonts; null while no project is open.
    public Media.FontManager? ProjectFonts => Sessions.ActiveSession.Value?.ProjectFonts;

    // The fonts the scenes of a project resolve: the fork of its session while it is loaded, else the host's.
    internal Media.FontManager GetFonts(Project? project)
        => project is not null && Sessions.Find(project) is { } session
            ? session.ProjectFonts
            : Fonts;

    public ProjectService(ViewConfig configuration, Media.FontManager fonts, INotificationService notifications)
        : this(configuration, fonts, notifications, ProjectSessionRegistry.Current)
    {
    }

    internal ProjectService(ViewConfig configuration, Media.FontManager fonts, INotificationService notifications,
        EditorService editorService) : this(configuration, fonts, notifications)
    {
        _editorService = editorService;
    }

    internal ProjectService(ViewConfig configuration, Media.FontManager fonts, INotificationService notifications,
        ProjectSessionRegistry sessions)
    {
        _notifications = notifications;
        Fonts = fonts;
        _configuration = configuration;
        Sessions = sessions;
        _isOpened = CurrentProject.Select(v => v != null).ToReadOnlyReactivePropertySlim();
    }

    // The loaded projects. Every ProjectService of the application shares them.
    internal ProjectSessionRegistry Sessions { get; }

    /// <summary>
    /// Raised before <see cref="Closing"/>, while the editors of the session are still open. Anything that has to
    /// read or persist live editor state has to run here: <see cref="Closing"/> is where the editor host puts the
    /// tabs of a deactivated session aside, or disposes those of a closed one.
    /// </summary>
    /// <remarks>
    /// The chain runs both to deactivate a session (<see cref="ProjectCloseKind.Deactivate"/>), which keeps it
    /// loaded, and to close one (<see cref="ProjectCloseKind.Close"/>). A handler releases resources only on a close.
    /// A close can concern a session the editor does not show.
    /// </remarks>
    internal event Func<ProjectCloseContext, CancellationToken, Task>? ClosingPreparing;

    internal event Func<ProjectCloseContext, CancellationToken, Task>? Closing;

    internal event Func<ProjectCloseContext, CancellationToken, Task>? ClosingFinalizing;

    // Raised for every request to open a project file, before the file is read or a loaded session is reused.
    internal event Func<string, Task>? Opening;

    // Raised once a session becomes the active one, whether it was just read from its file or was loaded already.
    internal event Func<Project, Task>? Opened;

    internal event Action<ProjectTransitionCommit>? TransitionCommitted;

    // The project of the active session.
    public IReadOnlyReactiveProperty<Project?> CurrentProject => Sessions.ActiveProject;

    public IReadOnlyReactiveProperty<bool> IsOpened => _isOpened;

    public async Task OpenProject(string file)
    {
        _ = await EnsureSessionAsync(file);
    }

    // Shows the project at file: activates its session when it is loaded already, and reads it from the file
    // otherwise. The session that was active stays loaded with its edits. Returns the session, or null when the
    // request was superseded or failed; a newer request for another file takes the activation, and this one
    // then only loads its project.
    internal async Task<ProjectSession?> EnsureSessionAsync(string file)
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
                return null;
            }

            // Reported before taking a transition, so a deleted project holds up no other project change.
            if (!File.Exists(file))
            {
                ReportUnavailableProjectFile(file);
                return null;
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
                return null;
            }

            await using (transition)
            {
                if (!attempt.TryBeginApply())
                {
                    return null;
                }

                return await OpenProjectCoreAsync(
                    file,
                    transition.Context,
                    activate: IsNewestOpenAttempt(attempt));
            }
        }
        finally
        {
            CompleteOpenAttempt(attempt);
        }
    }

    // Closes every loaded session, the inactive ones first, for shutdown. A veto or failed save stops it with the
    // remaining sessions loaded.
    internal void CloseAllProjectsOrThrow()
    {
        Task close = CloseAllProjectsAsync();
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

    // Closes the active session; the other sessions stay loaded.
    internal Task CloseProjectAsync(CancellationToken cancellationToken = default)
        => CloseProjectWithCallbackAsync(null, cancellationToken);

    // Closes every loaded session; beforeCommit runs before the last one, the active session, is unloaded.
    internal async Task CloseAllProjectsAsync(
        Func<Task<bool>>? beforeCommit = null,
        CancellationToken cancellationToken = default)
    {
        await using ProjectTransitionScope transition = await BeginTransitionAsync(
            ProjectTransitionPurpose.Normal,
            this,
            cancellationToken);
        foreach (ProjectSession session in Sessions.Sessions.ToArray())
        {
            if (!ReferenceEquals(session, Sessions.ActiveSession.Value))
            {
                await CloseSessionCoreAsync(session, transition.Context, cancellationToken);
            }
        }

        await CloseSessionCoreAsync(
            Sessions.ActiveSession.Value,
            transition.Context,
            cancellationToken,
            beforeCommit: beforeCommit);
    }

    internal Task CloseAllProjectsForUpdateAsync(Func<Task<bool>> beforeCommit)
        => CloseAllProjectsAsync(beforeCommit, CancellationToken.None);

    private async Task CloseProjectWithCallbackAsync(Func<Task<bool>>? beforeCommit, CancellationToken cancellationToken)
    {
        await using ProjectTransitionScope transition = await BeginTransitionAsync(
            ProjectTransitionPurpose.Normal,
            this,
            cancellationToken);
        await CloseSessionCoreAsync(
            Sessions.ActiveSession.Value,
            transition.Context,
            cancellationToken,
            beforeCommit: beforeCommit);
    }

    // Closes the session of expectedProject, active or not, unless the close is vetoed. False when the project is
    // not loaded or the close was aborted.
    internal async Task<bool> TryCloseProjectAsync(
        Project expectedProject,
        ProjectCloseIntent closeIntent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedProject);
        try
        {
            // Unloading a session the editor does not show must not cancel a request to open another project.
            bool targetsActiveSession = ReferenceEquals(CurrentProject.Value, expectedProject);
            await using ProjectTransitionScope transition = await BeginTransitionAsync(
                ProjectTransitionPurpose.Normal,
                this,
                cancellationToken,
                cancelPendingOpens: targetsActiveSession);
            if (Sessions.Find(expectedProject) is not { } session)
            {
                return false;
            }

            await CloseSessionCoreAsync(session, transition.Context, cancellationToken, closeIntent);
            return true;
        }
        catch (ProjectCloseAbortedException)
        {
            return false;
        }
    }

    public Task<Project?> CreateProject(int width, int height, Rational framerate, int samplerate, string name, string location)
    {
        return CreateProject(width, height, framerate, samplerate, name, location, beforeOpening: null);
    }

    public Task<Project?> CreateProject(int width, int height, Rational framerate, int samplerate,
        string name, string location, SpeakerLayout channelLayout)
        => CreateProject(width, height, framerate, samplerate, name, location, null, channelLayout);

    // beforeOpening runs once the project files are written and before the project opens, inside the same
    // transition, so the editor opens only after it completes. Its token is canceled when another project
    // transition is requested meanwhile; the project then opens without waiting for the rest of the step.
    internal async Task<Project?> CreateProject(
        int width,
        int height,
        Rational framerate,
        int samplerate,
        string name,
        string location,
        Func<Project, CancellationToken, Task>? beforeOpening,
        SpeakerLayout channelLayout = SpeakerLayout.Stereo)
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
            channelLayout,
            creation.PreparationCancellation,
            transition.Context);
    }

    // Loads the project at file unless a session holds it already, and activates it when activate is set. A switch
    // deactivates the active session instead of closing it. On failure the previous session is active again, with
    // the very model, tabs and history it had, and a project read here is unloaded again.
    private async Task<ProjectSession?> OpenProjectCoreAsync(
        string file,
        ProjectTransitionContext transition,
        bool activate = true)
    {
        VerifyTransition(transition);
        await App.WaitLoadingExtensions();

        using Activity? activity = Telemetry.StartActivity();
        using UsageTelemetry.Operation? usage = UsageTelemetry.Current?.Begin("project.open");
        ProjectSession? previous = null;
        ProjectSession? loaded = null;
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
                ReportUnavailableProjectFile(file);
                usage?.Complete("skipped");
                return null;
            }

            ProjectSession? target = Sessions.Find(file);
            // Selecting the active project from Recents must not replace in-memory edits with a snapshot read from
            // its file, and neither may selecting any other loaded project: its session is reused as it is.
            if (target is not null && ReferenceEquals(target, Sessions.ActiveSession.Value))
            {
                TryAddToRecentProjects(file);
                usage?.Complete("skipped");
                return target;
            }

            Uri projectUri = UriHelper.CreateFromPath(file);
            if (!activate)
            {
                // A newer request takes the activation. This one still loads its project, which stays in the
                // background.
                if (target is null)
                {
                    target = LoadSession(CoreSerializer.RestoreFromUri<Project>(projectUri));
                    _logger.LogInformation("Loaded project without activating it. File: {File}", file);
                }

                usage?.Complete("skipped");
                return target;
            }

            previous = Sessions.ActiveSession.Value;
            if (target is null)
            {
                if (previous is not null)
                {
                    // Validate before deactivating so an invalid project, or one that needs a newer Beutl, leaves
                    // the current one active. Deactivating can save scenes or sidecars shared with this project
                    // (a version-control snapshot does), so discard the preflight graph and load the committed
                    // files once the deactivation completes.
                    _ = CoreSerializer.RestoreFromUri<Project>(projectUri);
                    await DeactivateSessionCoreAsync(previous, transition);
                }

                Project project = CoreSerializer.RestoreFromUri<Project>(projectUri);
                activity?.SetTag("appVersion", project.AppVersion);
                activity?.SetTag("minVersion", project.MinAppVersion);
                loaded = target = LoadSession(project);
            }
            else if (previous is not null)
            {
                await DeactivateSessionCoreAsync(previous, transition);
            }

            transition.TargetSession = target;
            await ActivateSessionCoreAsync(target, freshlyLoaded: loaded is not null);

            _logger.LogInformation(
                "Opened project. File: {File}, AppVersion: {AppVersion}, MinVersion: {MinVersion}, Reused: {Reused}",
                file,
                target.Project.AppVersion,
                target.Project.MinAppVersion,
                loaded is null);
            usage?.Complete();
            return target;
        }
        catch (Exception ex)
        {
            // The project file, or a file it references, needs a newer Beutl.
            UnsupportedFileVersionException? unsupported = ExceptionHelpers.Find<UnsupportedFileVersionException>(ex);
            if (unsupported is null)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                _logger.LogError(ex, "Unable to open the project. File: {File}", file);
            }
            else
            {
                _logger.LogInformation("Skipping project open: {Message}", unsupported.Message);
            }

            // A project read for this request goes again; one that was loaded before stays loaded.
            if (loaded is not null && !ReferenceEquals(Sessions.ActiveSession.Value, loaded))
            {
                Sessions.Remove(loaded);
            }

            if (previous is not null
                && Sessions.ActiveSession.Value is null
                && Sessions.Sessions.Contains(previous))
            {
                try
                {
                    // A restore or activation can fail after the previous session was deactivated. Activate that
                    // session again, so even its unsaved edits, tabs and history survive the failure.
                    await ActivateSessionCoreAsync(previous, freshlyLoaded: false);
                }
                catch (Exception recoveryFailure)
                {
                    _logger.LogError(recoveryFailure, "Unable to restore the previous project after opening {File} failed.", file);
                }
            }

            if (unsupported is not null)
            {
                var dialog = new FAContentDialog
                {
                    Title = MessageStrings.ProjectVersionMismatch_Title,
                    Content = string.Format(MessageStrings.ProjectVersionMismatch_Content, unsupported.RequiredVersion),
                    PrimaryButtonText = Strings.Close
                };
                await dialog.ShowAsync();
                usage?.Complete("skipped");
            }
            else
            {
                _notifications.ShowInformation(Strings.Project, MessageStrings.FailedToOpenProject);
            }

            return null;
        }
    }

    // Runs the close chain as a deactivation: the session stays loaded with its edits and the editor stops showing
    // it. A handler can veto it with ProjectCloseAbortedException, which leaves the session active.
    private async Task DeactivateSessionCoreAsync(
        ProjectSession session,
        ProjectTransitionContext transition,
        CancellationToken cancellationToken = default)
    {
        VerifyTransition(transition);
        if (!ReferenceEquals(Sessions.ActiveSession.Value, session))
        {
            return;
        }

        var closeContext = new ProjectCloseContext(session, ProjectCloseKind.Deactivate, ProjectCloseIntent.SaveChanges);
        try
        {
            await InvokeCloseHandlersAsync(ClosingPreparing, closeContext, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await InvokeCloseHandlersAsync(Closing, closeContext, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await NotifyClosingFinalizingAsync(closeContext);
            if (ReferenceEquals(Sessions.ActiveSession.Value, session))
            {
                Sessions.Activate(null);
                Interlocked.Exchange(ref _projectNoticeDismissal, null)?.Cancel();
                _logger.LogInformation("Deactivated project. Project: {Project}", session.Project.Uri);
                PublishTransitionCommitted(new ProjectTransitionCommit(
                    ProjectSessionChange.Deactivated,
                    session,
                    ActiveSessionChanged: true,
                    ActiveProject: null));
            }
        }
        finally
        {
            bool deactivated = !ReferenceEquals(Sessions.ActiveSession.Value, session);
            await closeContext.CompleteAsync(deactivated, _logger);
        }
    }

    // Runs the close chain for session, the active one or any other, and unloads it. Nothing happens for a session
    // that is not loaded, or null, except that beforeCommit still has to agree.
    private async Task CloseSessionCoreAsync(
        ProjectSession? session,
        ProjectTransitionContext transition,
        CancellationToken cancellationToken,
        ProjectCloseIntent closeIntent = ProjectCloseIntent.SaveChanges,
        Func<Task<bool>>? beforeCommit = null)
    {
        VerifyTransition(transition);
        if (session is null || !Sessions.Sessions.Contains(session))
        {
            if (beforeCommit is not null && !await beforeCommit())
                throw new ProjectCloseAbortedException(MessageStrings.OperationFailed);
            return;
        }

        transition.TargetSession ??= session;
        var closeContext = new ProjectCloseContext(session, ProjectCloseKind.Close, closeIntent);
        try
        {
            await InvokeCloseHandlersAsync(ClosingPreparing, closeContext, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await InvokeCloseHandlersAsync(Closing, closeContext, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await NotifyClosingFinalizingAsync(closeContext);
            // Keep the project published until the updater accepts the handoff. On
            // failure, the existing aborted-close completions restore its editor tabs.
            if (beforeCommit is not null && !await beforeCommit())
                throw new ProjectCloseAbortedException(MessageStrings.OperationFailed);
            CloseProjectImmediately(session, closeIntent);
        }
        finally
        {
            bool projectClosed = !Sessions.Sessions.Contains(session);
            await closeContext.CompleteAsync(projectClosed, _logger);
        }
    }

    // Unloads session without running the close chain. Every close path ends here, while the project is still whole.
    internal void CloseProjectImmediately(
        ProjectSession session,
        ProjectCloseIntent closeIntent = ProjectCloseIntent.SaveChanges)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!Sessions.Sessions.Contains(session))
        {
            return;
        }

        Project project = session.Project;
        bool wasActive = ReferenceEquals(Sessions.ActiveSession.Value, session);
        DuplicateIds.Report(project, "close");
        Sessions.Remove(session);
        if (wasActive)
        {
            Interlocked.Exchange(ref _projectNoticeDismissal, null)?.Cancel();
            try
            {
                _configuration.LastOpenedProjectFile.Value = null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clear the last-opened project setting.");
            }
        }

        _logger.LogInformation("Closed project. Project: {Project}", project.Uri);
        PublishTransitionCommitted(new ProjectTransitionCommit(
            closeIntent == ProjectCloseIntent.DiscardChanges ? ProjectSessionChange.Discarded : ProjectSessionChange.Closed,
            session,
            ActiveSessionChanged: wasActive,
            ActiveProject: Sessions.ActiveProject.Value));
    }

    private async Task<Project?> CreateProjectCoreAsync(
        int width,
        int height,
        Rational framerate,
        int samplerate,
        string name,
        string location,
        Func<Project, CancellationToken, Task>? beforeOpening,
        SpeakerLayout channelLayout,
        CancellationToken preparationCancellation,
        ProjectTransitionContext transition)
    {
        VerifyTransition(transition);
        await App.WaitLoadingExtensions();

        using Activity? activity = Telemetry.StartActivity();
        using UsageTelemetry.Operation? usage = UsageTelemetry.Current?.Begin("project.create");
        activity?.SetTag(nameof(width), width);
        activity?.SetTag(nameof(height), height);
        activity?.SetTag(nameof(framerate), framerate.ToDouble());
        activity?.SetTag(nameof(samplerate), samplerate);
        ProjectSession? previous = null;
        ProjectSession? created = null;
        try
        {
            string projectFile = Path.Combine(location, name, $"{name}.{EditorConstants.ProjectFileExtension}");
            // Writing the new files would pull them from under a project that is loaded from them.
            if (Sessions.Find(projectFile) is not null)
                throw new InvalidOperationException(MessageStrings.AlreadyExists);

            previous = Sessions.ActiveSession.Value;
            if (previous is not null)
                await DeactivateSessionCoreAsync(previous, transition);

            location = Path.Combine(location, name);
            var scene = new Scene(width, height, name)
            {
                Uri = UriHelper.CreateFromPath(Path.Combine(location, name, $"{name}.{EditorConstants.SceneFileExtension}")),
                FrameRate = { Value = framerate },
                SampleRate = { Value = samplerate },
                ChannelLayout = { Value = channelLayout },
            };
            var project = new Project()
            {
                Items = { scene },
                Uri = UriHelper.CreateFromPath(Path.Combine(location, $"{name}.{EditorConstants.ProjectFileExtension}")),
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

            created = LoadSession(project);
            transition.TargetSession = created;
            await ActivateSessionCoreAsync(created, freshlyLoaded: true);

            _logger.LogInformation("Created new project. Name: {Name}, Location: {Location}, Width: {Width}, Height: {Height}, Framerate: {Framerate}, Samplerate: {Samplerate}", name, location, width, height, framerate, samplerate);

            usage?.Complete();
            return project;
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException) usage?.Complete("cancelled");
            activity?.SetStatus(ActivityStatusCode.Error);
            _logger.LogError(ex, "Unable to create the project. Name: {Name}, Location: {Location}", name, location);
            if (created is not null && !ReferenceEquals(Sessions.ActiveSession.Value, created))
                Sessions.Remove(created);

            if (previous is not null
                && Sessions.ActiveSession.Value is null
                && Sessions.Sessions.Contains(previous))
            {
                try
                {
                    await ActivateSessionCoreAsync(previous, freshlyLoaded: false);
                }
                catch (Exception recoveryFailure)
                {
                    _logger.LogError(recoveryFailure, "Unable to restore the previous project after creating {Name} failed.", name);
                }
            }

            // Surface the actual failure (disk full, permission denied, ...) instead of a generic message.
            _notifications.ShowError(Strings.Error, ex.Message);
            return null;
        }
    }

    private void TryAddToRecentProjects(string file)
    {
        try
        {
            ViewConfig viewConfig = _configuration;
            viewConfig.UpdateRecentProject(file);
            viewConfig.UpdateRecentFile(file);
            viewConfig.LastOpenedProjectFile.Value = file;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update recent-project settings. File: {File}", file);
        }
    }

    private void ReportUnavailableProjectFile(string file)
    {
        _logger.LogInformation("Skipping project open: file is unavailable. File: {File}", file);
        _notifications.ShowInformation(Strings.File, MessageStrings.FileDoesNotExist);
    }

    // Loads project into a new session without activating it.
    private ProjectSession LoadSession(Project project)
    {
        Media.FontManager projectFonts = Fonts.Fork();
        projectFonts.LoadProjectFonts(project);
        return Sessions.Add(project, projectFonts);
    }

    // Makes session the active one and publishes it. On failure the session is deactivated again, and unloaded too
    // when freshlyLoaded says it was just read for this transition; the caller then restores what was active.
    private async Task ActivateSessionCoreAsync(ProjectSession session, bool freshlyLoaded)
    {
        Project project = session.Project;
        try
        {
            Sessions.Activate(session);
            await NotifyOpenedAsync(project);
        }
        catch
        {
            await RollBackFailedActivationAsync(
                session,
                freshlyLoaded ? ProjectCloseKind.Close : ProjectCloseKind.Deactivate);
            throw;
        }

        // The notices concern what the file held, so a session shows them once, when it is read.
        if (freshlyLoaded)
            ShowProjectNotices(project);

        if (session.FilePath is { } file)
            TryAddToRecentProjects(file);

        PublishTransitionCommitted(new ProjectTransitionCommit(
            ProjectSessionChange.Activated,
            session,
            ActiveSessionChanged: true,
            ActiveProject: project));
    }

    private void ShowProjectNotices(Project project)
    {
        Action? showDetails = _editorService is { } editorService ? () =>
        {
            if (ReferenceEquals(CurrentProject.Value, project))
                editorService.OpenUndisplayableData(project);
        }
        : null;
        Notification[] notices = new[]
            {
                Beutl.Editor.Services.UnmigratedNodeTreeNotice.Create(project, showDetails),
                Beutl.Editor.Services.KeptUnknownDataNotice.Create(project, showDetails),
            }
            .OfType<Notification>()
            .ToArray();
        if (notices.Length == 0) return;

        var dismissal = new CancellationTokenSource();
        Interlocked.Exchange(ref _projectNoticeDismissal, dismissal)?.Cancel();
        foreach (Notification notice in notices)
        {
            _notifications.Show(notice with { CancellationToken = dismissal.Token });
        }
    }

    // Takes back an activation whose Opened handlers failed: the close chain runs as a deactivation for a session
    // that was loaded before, which keeps its tabs and edits, and as a close for one just read from its file.
    private async Task RollBackFailedActivationAsync(ProjectSession session, ProjectCloseKind kind)
    {
        if (!ReferenceEquals(Sessions.ActiveSession.Value, session))
        {
            if (kind == ProjectCloseKind.Close)
                Sessions.Remove(session);
            return;
        }

        var closeContext = new ProjectCloseContext(session, kind, ProjectCloseIntent.SaveChanges);
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

            if (kind == ProjectCloseKind.Close)
                Sessions.Remove(session);
            else if (ReferenceEquals(Sessions.ActiveSession.Value, session))
                Sessions.Activate(null);
        }
        finally
        {
            bool rolledBack = !ReferenceEquals(Sessions.ActiveSession.Value, session);
            await closeContext.CompleteAsync(rolledBack, _logger);
        }
    }
}
