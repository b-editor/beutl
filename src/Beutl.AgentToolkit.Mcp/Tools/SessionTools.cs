using System.ComponentModel;
using System.Globalization;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;
using Beutl.ProjectSystem;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Mcp.Tools;

public sealed record SceneSummary(string SceneId, string Name, int Width, int Height, string Start, string Duration, int Elements);

public sealed record SessionSummary(IReadOnlyList<SceneSummary> Scenes);

public sealed record OpenProjectResponse(string Session, string Source, SessionSummary Summary);

public sealed record CreateProjectResponse(string Session, string SavedPath, SessionSummary Summary);

public sealed record AddSceneResponse(string SceneId, SessionSummary Summary);

public sealed record SaveProjectResponse(string SavedPath);

[McpServerToolType]
public sealed class SessionTools(
    FileSessionSource fileSessions,
    AgentSessionManager sessions,
    IWorkspaceGuard workspace,
    DestructiveGuard destructiveGuard) : ToolBase
{
    [McpServerTool(Name = "open_project")]
    [Description("Opens a Beutl project from any readable local path. Reads are unrestricted.")]
    public ToolResult<OpenProjectResponse> OpenProject(string path)
    {
        return Execute(() =>
        {
            string fullPath = Path.GetFullPath(path);
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
    [Description("Creates and saves a new project with one scene. The output path is restricted to BEUTL_WORKSPACE.")]
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
            string writePath = workspace.ResolveForWrite(path);
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
    [Description("Adds a scene to the current file-opened project. Persist with save_project.")]
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
    [Description("Saves the current file-opened project. Optional path is restricted to BEUTL_WORKSPACE.")]
    public ToolResult<SaveProjectResponse> SaveProject(
        string session,
        string? path = null,
        bool confirmOverwrite = false)
    {
        return Execute(() =>
        {
            FileEditingSession fileSession = RequireFileSession(session);
            bool skipConflictCheck = false;
            if (!string.IsNullOrWhiteSpace(path))
            {
                string writePath = workspace.ResolveForWrite(path);
                string currentPath = fileSession.Project.Uri?.LocalPath ?? string.Empty;
                if (!string.Equals(Path.GetFullPath(currentPath), Path.GetFullPath(writePath), StringComparison.OrdinalIgnoreCase))
                {
                    destructiveGuard.EnsureOverwriteAllowed(writePath, confirmOverwrite);
                    fileSession.SetProjectPath(writePath);
                    skipConflictCheck = confirmOverwrite;
                }
            }

            fileSession.Save(skipConflictCheck);
            return new SaveProjectResponse(fileSession.Project.Uri!.LocalPath);
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

    private static T Throw<T>(string code, string message, string? target = null)
    {
        throw new ReconcileException(new ToolError(code, message, target));
    }
}
