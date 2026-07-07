using System.ComponentModel;
using System.Globalization;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Workspace;
using Beutl.Editor;
using Beutl.ProjectSystem;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed record SceneSummary(string SceneId, string Name, int Width, int Height, string Start, string Duration, int Elements);

public sealed record SessionSummary(IReadOnlyList<SceneSummary> Scenes);

public sealed record OpenProjectResponse(string Session, string Source, SessionSummary Summary);

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
    IWorkspaceGuard workspace,
    DestructiveGuard destructiveGuard,
    RenderJobManager renderJobs) : ToolBase
{
    [McpServerTool(Name = "open_project")]
    [Description("Opens a Beutl .bep project from any readable local path and makes it the active editing session. In the in-app host this opens the project in the Beutl editor (the editor holds a single open project; the session is LiveEditor and edits show live); in the stdio host it opens a file-backed session.")]
    public ValueTask<ToolResult<OpenProjectResponse>> OpenProject(string path, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            // Resolve a relative path against the workspace root, matching create_project/save_project,
            // so the same path is not reported missing just because the MCP process cwd differs from
            // BEUTL_WORKSPACE. Absolute paths are honored as-is (open_project reads anywhere).
            string fullPath = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(workspace.Root, path));
            ValidateProjectFileExtension(fullPath, nameof(path));
            if (!File.Exists(fullPath))
            {
                return Throw<OpenProjectResponse>(ErrorCode.MediaNotFound, $"Project file not found: {fullPath}", fullPath);
            }

            ProjectSessionResult result = await projects.OpenProjectAsync(fullPath, cancellationToken).ConfigureAwait(false);
            return new OpenProjectResponse(
                result.Session.SessionId,
                result.Session.Source.ToString(),
                CreateSummary(result.Session, result.Project));
        });
    }

    [McpServerTool(Name = "create_project")]
    [Description("Creates and saves a new Beutl .bep project with one scene, then makes it the active editing session. In the in-app host the project opens in the Beutl editor (single open project, LiveEditor session); in the stdio host it becomes a file-backed session. Paths without an extension are saved as .bep; .beutl is reserved for project packages. The output path is restricted to BEUTL_WORKSPACE.")]
    public ValueTask<ToolResult<CreateProjectResponse>> CreateProject(
        [Description("Workspace-relative project file path; project files use path, while render/export outputs use outputPath/outputDirectory.")]
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
            string writePath = NormalizeProjectPath(workspace, path, nameof(path));
            destructiveGuard.EnsureOverwriteAllowed(writePath, confirmOverwrite);

            ProjectSessionResult result = await projects.CreateProjectAsync(
                new ProjectCreateOptions(
                    writePath,
                    width,
                    height,
                    frameRate,
                    ParseTimeSpan(duration, nameof(duration))),
                cancellationToken).ConfigureAwait(false);
            return new CreateProjectResponse(
                result.Session.SessionId,
                result.Project.Uri!.LocalPath,
                CreateSummary(result.Session, result.Project));
        });
    }

    [McpServerTool(Name = "add_scene")]
    [Description("Adds a scene to the current project. File-backed sessions persist it with save_project; in the in-app host the scene is saved and shown in the editor immediately.")]
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
            RequireActiveSession(session);
            ValidateProjectSettings(width, height, frameRate: 1);
            ProjectSceneResult result = await projects.AddSceneAsync(
                new SceneCreateOptions(
                    width,
                    height,
                    ParseTimeSpan(start, nameof(start)),
                    ParseTimeSpan(duration, nameof(duration)),
                    name),
                cancellationToken).ConfigureAwait(false);
            return new AddSceneResponse(
                result.Scene.Id.ToString(),
                result.Session.SessionId,
                CreateSummary(result.Session, result.Project));
        });
    }

    [McpServerTool(Name = "save_project")]
    [Description("Saves the current file-backed .bep project. Call after each major successful apply_edit in file-backed sessions so partial progress is durable, and again after final revisions. Optional paths without an extension are saved as .bep; .beutl is reserved for project packages. Optional path is restricted to BEUTL_WORKSPACE.")]
    public ToolResult<SaveProjectResponse> SaveProject(
        string? session = null,
        string? path = null,
        bool confirmOverwrite = false)
    {
        return Execute(() =>
        {
            IEditingSession activeSession = sessions.RequireSession();
            string sessionId = string.IsNullOrWhiteSpace(session)
                ? activeSession.SessionId
                : session;

            if (sessions.CurrentSession is { Source: EditingSessionSource.LiveEditor } liveSession)
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
            bool skipConflictCheck = false;
            if (!string.IsNullOrWhiteSpace(path))
            {
                string writePath = NormalizeProjectPath(workspace, path, nameof(path));
                string currentPath = fileSession.Project.Uri?.LocalPath ?? string.Empty;
                if (!string.Equals(Path.GetFullPath(currentPath), Path.GetFullPath(writePath), PathComparison.ForCurrentPlatform))
                {
                    destructiveGuard.EnsureOverwriteAllowed(writePath, confirmOverwrite);
                    fileSession.SetProjectPath(writePath);
                    skipConflictCheck = confirmOverwrite;
                }
            }

            fileSession.Save(skipConflictCheck);
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
    [Description("Reports the active Agent Editing Toolkit session state and whether save_project is supported. Toolkit edit/render calls are synchronous; use this when a workflow needs a quick status response before continuing or stopping.")]
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
                    Message: "No active editing session is available. Call attach_active_editor for an open editor scene, or create_project/open_project for a file-backed session.");
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
                    ? "Active file-backed session. MCP tool calls are synchronous; call save_project after major successful apply_edit stages."
                    : "Active LiveEditor session. MCP tool calls are synchronous; edits are already applied to the open editor and save_project is not required or supported.");
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

    private static void ValidateProjectSettings(int width, int height, int frameRate)
    {
        if (width <= 0 || height <= 0 || frameRate <= 0)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                "Width, height, and frame rate must be positive."));
        }
    }

    internal static string NormalizeProjectPath(IWorkspaceGuard workspace, string requestedPath, string target)
    {
        string candidate = requestedPath;
        string extension = Path.GetExtension(requestedPath);
        if (string.IsNullOrEmpty(extension))
        {
            candidate = $"{requestedPath}.{EditorConstants.ProjectFileExtension}";
        }
        else
        {
            ValidateProjectFileExtension(candidate, target);
        }

        // Resolve the final file (extension already appended) through the workspace guard so the
        // path actually written — following any symlink — is the one boundary-checked.
        string resolved = workspace.ResolveForWrite(candidate);
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
            $"Use a path ending in '{expected}', for example 'agent-output/example{expected}'."));
    }

    private static T Throw<T>(string code, string message, string? target = null)
    {
        throw new ReconcileException(new ToolError(code, message, target));
    }
}
