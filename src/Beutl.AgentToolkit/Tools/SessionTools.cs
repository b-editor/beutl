using System.Collections;
using System.ComponentModel;
using System.Globalization;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Workspace;
using Beutl.Animation;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed record SceneSummary(string SceneId, string Name, int Width, int Height, string Start, string Duration, int Elements);

public sealed record SessionSummary(IReadOnlyList<SceneSummary> Scenes);

public sealed record RecoveryIncident(
    string SceneId,
    string SceneName,
    string ElementFile,
    string Reason,
    string? TypeName,
    string? Message);

public sealed record OpenProjectResponse(string Session, string Source, SessionSummary Summary)
{
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<RecoveryIncident> RecoveryIncidents { get; init; } = [];

    /// <summary>
    /// The Beutl version that last saved the project, as its project file records it, or <see langword="null"/>
    /// when the file records none or the value is not a version.
    /// </summary>
    public string? LastSavedWith { get; init; }
}

public sealed record CreateProjectResponse(string Session, string SavedPath, SessionSummary Summary);

public sealed record AddSceneResponse(string SceneId, string Session, SessionSummary Summary);

public sealed record SaveProjectResponse(string SavedPath)
{
    public bool Saved { get; init; } = true;

    public string? Session { get; init; }

    public string? Source { get; init; }

    public string? Message { get; init; }
}

public sealed record OperationStatusResponse(
    bool HasActiveSession,
    string? Session,
    string? Source,
    bool? IsDirty,
    bool SaveProjectSupported,
    bool HasLongRunningOperation,
    string Message);

[McpServerToolType]
public sealed class SessionTools(
    IProjectSessionGateway projects,
    AgentSessionManager sessions,
    DestructiveGuard destructiveGuard,
    RenderJobManager renderJobs) : ToolBase
{
    [McpServerTool(Name = "open_project")]
    [Description("Opens a Beutl .bep project from an absolute local path and returns its scene IDs. Live MCP calls must still pass sceneId on each subsequent scene operation; stdio selects a file-backed session. In the in-app host this opens the project in the Beutl editor (the editor holds a single open project; the session is LiveEditor and edits show live); in the stdio host it opens a file-backed session.")]
    public ValueTask<ToolResult<OpenProjectResponse>> OpenProject(
        [Description("Absolute path of the .bep project file.")]
        string path,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            string fullPath = ToolPaths.RequireAbsolute(path, nameof(path));
            ValidateProjectFileExtension(fullPath, nameof(path));
            if (!File.Exists(fullPath))
            {
                throw new ReconcileException(new ToolError(ErrorCode.MediaNotFound, $"Project file not found: {fullPath}", fullPath));
            }

            ProjectSessionResult result = await projects.OpenProjectAsync(fullPath, cancellationToken).ConfigureAwait(false);
            DeserializationWarningCollection recovery = result.Session.ReadOnSession(
                () => CollectDeserializationWarnings(result.Project));
            return new OpenProjectResponse(
                result.Session.SessionId,
                result.Session.Source.ToString(),
                CreateSummary(result.Session, result.Project))
            {
                Warnings = recovery.Warnings,
                RecoveryIncidents = recovery.RecoveryIncidents,
                LastSavedWith = result.Session.ReadOnSession(() => RootFileVersions.GetLastSavedVersion(result.Project)),
            };
        });
    }

    private static DeserializationWarningCollection CollectDeserializationWarnings(Project project)
    {
        var warnings = new List<string>();
        var incidents = new List<RecoveryIncident>();
        // The notice the editor shows when it opens the project. The version it names comes from the file, so it
        // is returned in LastSavedWith rather than in this text.
        if (RootFileVersions.HasKeptUnknownData(project))
        {
            warnings.Add(
                "This project contains data from a newer version of Beutl or from an extension that is not installed. "
                + "Beutl keeps that data when it saves the project, but does not show or use it, and documents leave out "
                + "the members it does not know. The lastSavedWith field names the version that last saved the "
                + "project, when the project file records one.");
        }

        foreach (Scene scene in project.Items.OfType<Scene>())
        {
            foreach (Element element in scene.Children)
            {
                IFallback[] fallbacks = SerializedGraphTraversal.Enumerate(element)
                    .OfType<IFallback>()
                    .ToArray();

                string elementFile = DescribeElementFile(scene, element);
                AddFallbackIncidents(scene, elementFile, fallbacks, incidents);
                AddSuppressedRecoveryIncidents(scene, elementFile, element, incidents);
            }
        }

        return new DeserializationWarningCollection(warnings, incidents);
    }

    private static string DescribeElementFile(Scene scene, Element element)
    {
        return element.Uri is { IsFile: true } uri
            && scene.Uri is { IsFile: true } sceneUri
            ? Path.GetRelativePath(
                Path.GetDirectoryName(sceneUri.LocalPath)!,
                uri.LocalPath).Replace('\\', '/')
            : element.Name;
    }

    private static void AddFallbackIncidents(
        Scene scene,
        string elementFile,
        IFallback[] fallbacks,
        List<RecoveryIncident> incidents)
    {
        foreach (IFallback fallback in fallbacks)
        {
            fallback.TryGetTypeName(out string? typeName);
            if (string.Equals(
                    typeName,
                    IdentityHelper.WriteDiscriminator(fallback.GetType()),
                    StringComparison.Ordinal))
            {
                typeName = null;
            }

            incidents.Add(new RecoveryIncident(
                scene.Id.ToString(),
                scene.Name,
                elementFile,
                fallback.Reason.ToString(),
                typeName,
                fallback.ErrorMessage));
        }
    }

    private static void AddSuppressedRecoveryIncidents(
        Scene scene,
        string elementFile,
        Element element,
        List<RecoveryIncident> incidents)
    {
        if (element.SuppressedStorageSource is { HasNonFallbackIncidents: true } source)
        {
            foreach (SuppressedRecoveryIncident recoveryIncident in source.RecoveryIncidents!)
            {
                incidents.Add(new RecoveryIncident(
                    scene.Id.ToString(),
                    scene.Name,
                    elementFile,
                    recoveryIncident.Reason,
                    recoveryIncident.TypeName,
                    recoveryIncident.Message));
            }
        }
    }

    private sealed record DeserializationWarningCollection(
        IReadOnlyList<string> Warnings,
        IReadOnlyList<RecoveryIncident> RecoveryIncidents);

    [McpServerTool(Name = "create_project")]
    [Description("Creates and saves a new Beutl .bep project with one scene and returns its scene ID. Live MCP calls must still pass sceneId on each subsequent scene operation; stdio selects a file-backed session. In the in-app host the project opens in the Beutl editor (single open project, LiveEditor session); in the stdio host it becomes a file-backed session. Paths without an extension are saved as .bep; .beutl is reserved for project packages.")]
    public ValueTask<ToolResult<CreateProjectResponse>> CreateProject(
        [Description("Absolute project file path; project files use path, while render/export outputs use outputPath/outputDirectory.")]
        string path,
        int width,
        int height,
        int frameRate,
        string duration,
        bool confirmOverwrite = false,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            ValidateProjectSettings(width, height, frameRate);
            string writePath = NormalizeProjectPath(path, nameof(path));
            destructiveGuard.EnsureOverwriteAllowed(writePath, confirmOverwrite);

            ProjectSessionResult result = await projects.CreateProjectAsync(
                new ProjectCreateOptions(
                    writePath,
                    width,
                    height,
                    frameRate,
                    ParsePositiveTimeSpan(duration, nameof(duration))),
                cancellationToken).ConfigureAwait(false);
            return new CreateProjectResponse(
                result.Session.SessionId,
                result.Project.Uri!.LocalPath,
                CreateSummary(result.Session, result.Project));
        });
    }

    [McpServerTool(Name = "add_scene")]
    [Description("Adds a scene to the targeted project. Live MCP targets an existing scene with sceneId and returns the new sceneId; later calls must still specify their target. File-backed sessions persist it with save_project; in the in-app host the scene is saved immediately without selecting its editor tab.")]
    public ValueTask<ToolResult<AddSceneResponse>> AddScene(
        string session,
        int width,
        int height,
        string start,
        string duration,
        string? name = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            IEditingSession activeSession = RequireActiveSession(session);
            ValidateProjectSettings(width, height, frameRate: 1);
            ProjectSceneResult result = await projects.AddSceneAsync(
                activeSession,
                new SceneCreateOptions(
                    width,
                    height,
                    ParseNonNegativeTimeSpan(start, nameof(start)),
                    ParsePositiveTimeSpan(duration, nameof(duration)),
                    name),
                cancellationToken).ConfigureAwait(false);
            return new AddSceneResponse(
                result.Scene.Id.ToString(),
                result.Session.SessionId,
                CreateSummary(result.Session, result.Project));
        });
    }

    [McpServerTool(Name = "save_project")]
    [Description("Saves the current file-backed .bep project. Call after each major successful apply_edit in file-backed sessions so partial progress is durable, and again after final revisions. Optional paths without an extension are saved as .bep; .beutl is reserved for project packages.")]
    public ToolResult<SaveProjectResponse> SaveProject(
        string? session = null,
        [Description("Optional absolute path to save the project to instead of its current file.")]
        string? path = null,
        bool confirmOverwrite = false)
    {
        return Execute(() =>
        {
            IEditingSession activeSession = sessions.RequireSession();
            string sessionId = string.IsNullOrWhiteSpace(session)
                ? activeSession.SessionId
                : session;

            if (activeSession is { Source: EditingSessionSource.LiveEditor } liveSession)
            {
                if (!string.Equals(liveSession.SessionId, sessionId, StringComparison.Ordinal))
                {
                    throw new ReconcileException(new ToolError(
                        ErrorCode.StaleHandle,
                        $"Session '{sessionId}' is not active.",
                        sessionId));
                }

                return new SaveProjectResponse(string.Empty)
                {
                    Saved = false,
                    Session = liveSession.SessionId,
                    Source = liveSession.Source.ToString(),
                    Message = "Live editor sessions apply edits directly to the open editor; save_project is not required or supported by the Agent Editing Toolkit for LiveEditor. Save from the Beutl UI if you need to persist the open project."
                };
            }

            FileEditingSession fileSession = RequireFileSession(sessionId);
            bool savedAs = false;
            if (!string.IsNullOrWhiteSpace(path))
            {
                string writePath = NormalizeProjectPath(path, nameof(path));
                string currentPath = fileSession.Project.Uri?.LocalPath ?? string.Empty;
                if (!FilePathComparison.AreSameCanonicalPath(currentPath, writePath))
                {
                    destructiveGuard.EnsureOverwriteAllowed(writePath, confirmOverwrite);
                    fileSession.SaveAs(writePath, skipConflictCheck: confirmOverwrite);
                    savedAs = true;
                }
            }

            if (!savedAs)
            {
                fileSession.Save(skipConflictCheck: false);
            }

            return new SaveProjectResponse(fileSession.Project.Uri!.LocalPath)
            {
                Saved = true,
                Session = fileSession.SessionId,
                Source = fileSession.Source.ToString(),
                Message = "File-backed project saved."
            };
        });
    }

    [McpServerTool(Name = "read_operation_status")]
    [Description("Reports the targeted scene context and whether save_project is supported. Live MCP accepts optional sceneId; omitting it reports no scene context and never inherits an earlier call. Stdio reports its current file session. Use this when a workflow needs a quick status response before continuing or stopping.")]
    public ToolResult<OperationStatusResponse> ReadOperationStatus()
    {
        return Execute(() =>
        {
            IEditingSession? session = sessions.CurrentSession;
            if (session is null)
            {
                return new OperationStatusResponse(
                    HasActiveSession: false,
                    Session: null,
                    Source: null,
                    IsDirty: null,
                    SaveProjectSupported: false,
                    HasLongRunningOperation: renderJobs.HasRunningJobs,
                    Message: "No scene was targeted for this call. In live MCP, pass sceneId from list_scenes; in stdio MCP, call create_project/open_project for a file-backed session.");
            }

            bool fileBacked = session.Source == EditingSessionSource.File;
            return new OperationStatusResponse(
                HasActiveSession: true,
                Session: session.SessionId,
                Source: session.Source.ToString(),
                IsDirty: session.IsDirty,
                SaveProjectSupported: fileBacked,
                HasLongRunningOperation: renderJobs.HasRunningJobs,
                Message: fileBacked
                    ? "Active file-backed session. Call save_project after major successful apply_edit stages."
                    : "Active LiveEditor session. Edits are already applied to the open editor and save_project is not required or supported.");
        });
    }

    private FileEditingSession RequireFileSession(string sessionId)
    {
        if (RequireActiveSession(sessionId) is not FileEditingSession session)
        {
            throw new SessionUnavailableException();
        }

        return session;
    }

    private IEditingSession RequireActiveSession(string sessionId)
    {
        IEditingSession session = sessions.RequireSession();
        if (!string.Equals(session.SessionId, sessionId, StringComparison.Ordinal))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.StaleHandle,
                $"Session '{sessionId}' is not active.",
                sessionId));
        }

        return session;
    }

    private static SessionSummary CreateSummary(IEditingSession session, Project project)
    {
        return session.ReadOnSession(() => CreateSummary(project));
    }

    private static SessionSummary CreateSummary(Project project)
    {
        return new SessionSummary(project.Items
            .OfType<Scene>()
            .Select(scene => new SceneSummary(
                scene.Id.ToString(),
                scene.Name,
                scene.FrameSize.Width,
                scene.FrameSize.Height,
                scene.Start.ToString("c", CultureInfo.InvariantCulture),
                scene.Duration.ToString("c", CultureInfo.InvariantCulture),
                scene.Children.Count))
            .ToArray());
    }

    private static TimeSpan ParseTimeSpan(string value, string field)
    {
        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out TimeSpan result))
        {
            return result;
        }

        throw new ReconcileException(new ToolError(
            ErrorCode.ValidationRejected,
            $"'{field}' is not a valid time span (expected an invariant TimeSpan string such as '00:00:05').",
            field));
    }

    // Scene.Start/Duration do not clamp, so a negative or zero value would persist a scene whose
    // range sampling and export read before the timeline instead of rejecting the request.
    private static TimeSpan ParseNonNegativeTimeSpan(string value, string field)
    {
        TimeSpan result = ParseTimeSpan(value, field);
        if (result < TimeSpan.Zero)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"'{field}' must be a non-negative time span.",
                field));
        }

        return result;
    }

    private static TimeSpan ParsePositiveTimeSpan(string value, string field)
    {
        TimeSpan result = ParseTimeSpan(value, field);
        if (result <= TimeSpan.Zero)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"'{field}' must be a positive time span.",
                field));
        }

        return result;
    }

    private static void ValidateProjectSettings(int width, int height, int frameRate)
    {
        if (width <= 0 || height <= 0 || frameRate <= 0)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                "Width, height, and frame rate must be positive."));
        }
    }

    internal static string NormalizeProjectPath(string requestedPath, string target)
    {
        string candidate = ToolPaths.RequireAbsolute(requestedPath, target);
        string extension = Path.GetExtension(candidate);
        if (string.IsNullOrEmpty(extension))
        {
            candidate = $"{candidate}.{EditorConstants.ProjectFileExtension}";
        }
        else
        {
            ValidateProjectFileExtension(candidate, target);
        }

        // Resolve the final file (extension already appended) so the extension is checked on the
        // path actually written, after following any symlink.
        string resolved = ToolPaths.ResolveForWrite(candidate, target);
        ValidateProjectFileExtension(resolved, target);
        return resolved;
    }

    private static void ValidateProjectFileExtension(string path, string target)
    {
        string expected = $".{EditorConstants.ProjectFileExtension}";
        string actual = Path.GetExtension(path);
        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new ReconcileException(new ToolError(
            ErrorCode.ValidationRejected,
            $"File-backed Beutl projects must use the '{expected}' extension. The '.{EditorConstants.ProjectPackageExtension}' extension is reserved for exported project packages.",
            target,
            $"Use a path ending in '{expected}', for example 'example{expected}'."));
    }
}
