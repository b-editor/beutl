using System.ComponentModel;
using System.Globalization;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
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

public sealed record AddSceneResponse(string SceneId, SessionSummary Summary);

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
    FileSessionSource fileSessions,
    AgentSessionManager sessions,
    IWorkspaceGuard workspace,
    DestructiveGuard destructiveGuard) : ToolBase
{
    [McpServerTool(Name = "open_project")]
    [Description("Opens a Beutl .bep project from any readable local path and makes it the active file-backed editing session.")]
    public ToolResult<OpenProjectResponse> OpenProject(string path)
    {
        return Execute(() =>
        {
            string fullPath = Path.GetFullPath(path);
            ValidateProjectFileExtension(fullPath, nameof(path));
            if (!File.Exists(fullPath))
            {
                return Throw<OpenProjectResponse>(ErrorCode.MediaNotFound, $"Project file not found: {fullPath}", fullPath);
            }

            FileEditingSession session = fileSessions.OpenProject(fullPath);
            sessions.UseSource(fileSessions);
            return new OpenProjectResponse(session.SessionId, session.Source.ToString(), CreateSummary(session.Project));
        });
    }

    [McpServerTool(Name = "create_project")]
    [Description("Creates and saves a new file-backed Beutl .bep project with one scene. Paths without an extension are saved as .bep; .beutl is reserved for project packages. The output path is restricted to BEUTL_WORKSPACE.")]
    public ToolResult<CreateProjectResponse> CreateProject(
        string path,
        int width,
        int height,
        int frameRate,
        string duration,
        bool confirmOverwrite = false)
    {
        return Execute(() =>
        {
            ValidateProjectSettings(width, height, frameRate);
            string writePath = NormalizeProjectPath(workspace.ResolveForWrite(path), nameof(path));
            destructiveGuard.EnsureOverwriteAllowed(writePath, confirmOverwrite);

            FileEditingSession session = fileSessions.CreateProject(new ProjectCreateOptions(
                writePath,
                width,
                height,
                frameRate,
                ParseTimeSpan(duration)));
            sessions.UseSource(fileSessions);
            session.Save(skipConflictCheck: true);
            return new CreateProjectResponse(session.SessionId, session.Project.Uri!.LocalPath, CreateSummary(session.Project));
        });
    }

    [McpServerTool(Name = "add_scene")]
    [Description("Adds a scene to the current file-backed project. Persist with save_project.")]
    public ToolResult<AddSceneResponse> AddScene(
        string session,
        int width,
        int height,
        string start,
        string duration,
        string? name = null)
    {
        return Execute(() =>
        {
            FileEditingSession fileSession = RequireFileSession(session);
            ValidateProjectSettings(width, height, frameRate: 1);
            Scene scene = fileSessions.AddScene(new SceneCreateOptions(
                width,
                height,
                ParseTimeSpan(start),
                ParseTimeSpan(duration),
                name));
            return new AddSceneResponse(scene.Id.ToString(), CreateSummary(fileSession.Project));
        });
    }

    [McpServerTool(Name = "save_project")]
    [Description("Saves the current file-backed .bep project. Call after each major successful apply_edit in file-backed sessions so partial progress is durable, and again after final revisions. Optional paths without an extension are saved as .bep; .beutl is reserved for project packages. Optional path is restricted to BEUTL_WORKSPACE.")]
    public ToolResult<SaveProjectResponse> SaveProject(
        string session,
        string? path = null,
        bool confirmOverwrite = false)
    {
        return Execute(() =>
        {
            if (sessions.CurrentSession is { Source: EditingSessionSource.LiveEditor } liveSession)
            {
                if (!string.Equals(liveSession.SessionId, session, StringComparison.Ordinal))
                {
                    throw new ReconcileException(new ToolError(
                        ErrorCode.StaleHandle,
                        $"Session '{session}' is not active.",
                        session));
                }

                return new SaveProjectResponse(string.Empty)
                {
                    Saved = false,
                    Session = liveSession.SessionId,
                    Source = liveSession.Source.ToString(),
                    Message = "Live editor sessions apply edits directly to the open editor; save_project is not required or supported by the Agent Editing Toolkit for LiveEditor. Save from the Beutl UI if you need to persist the open project."
                };
            }

            FileEditingSession fileSession = RequireFileSession(session);
            bool skipConflictCheck = false;
            if (!string.IsNullOrWhiteSpace(path))
            {
                string writePath = NormalizeProjectPath(workspace.ResolveForWrite(path), nameof(path));
                string currentPath = fileSession.Project.Uri?.LocalPath ?? string.Empty;
                if (!string.Equals(Path.GetFullPath(currentPath), Path.GetFullPath(writePath), StringComparison.OrdinalIgnoreCase))
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
                    HasLongRunningOperation: false,
                    Message: "No active editing session is available. Call attach_active_editor for an open editor scene, or create_project/open_project for a file-backed session.");
            }

            bool fileBacked = session.Source == EditingSessionSource.File;
            return new OperationStatusResponse(
                HasActiveSession: true,
                Session: session.SessionId,
                Source: session.Source.ToString(),
                IsDirty: session.IsDirty,
                SaveProjectSupported: fileBacked,
                HasLongRunningOperation: false,
                Message: fileBacked
                    ? "Active file-backed session. MCP tool calls are synchronous; call save_project after major successful apply_edit stages."
                    : "Active LiveEditor session. MCP tool calls are synchronous; edits are already applied to the open editor and save_project is not required or supported.");
        });
    }

    private FileEditingSession RequireFileSession(string sessionId)
    {
        if (fileSessions.CurrentFileSession is not { } session)
        {
            throw new SessionUnavailableException();
        }

        if (!string.Equals(session.SessionId, sessionId, StringComparison.Ordinal))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.StaleHandle,
                $"Session '{sessionId}' is not active.",
                sessionId));
        }

        sessions.UseSource(fileSessions);
        return session;
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

    private static TimeSpan ParseTimeSpan(string value)
    {
        return TimeSpan.Parse(value, CultureInfo.InvariantCulture);
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

    internal static string NormalizeProjectPath(string path, string target)
    {
        string fullPath = Path.GetFullPath(path);
        string extension = Path.GetExtension(fullPath);
        if (string.IsNullOrEmpty(extension))
        {
            return $"{fullPath}.{EditorConstants.ProjectFileExtension}";
        }

        ValidateProjectFileExtension(fullPath, target);
        return fullPath;
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
