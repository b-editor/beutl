using Beutl.AgentToolkit.Workspace;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Sessions;

public sealed record ProjectSessionResult(IEditingSession Session, Project Project);

public sealed record ProjectSceneResult(Scene Scene, Project Project, IEditingSession Session);

/// <summary>
/// Host seam for open_project/create_project/add_scene: stdio serves file-backed
/// sessions; the in-app host opens the editor's single project as a live session.
/// </summary>
public interface IProjectSessionGateway
{
    ValueTask<ProjectSessionResult> OpenProjectAsync(string fullPath, CancellationToken cancellationToken = default);

    ValueTask<ProjectSessionResult> CreateProjectAsync(ProjectCreateOptions options, CancellationToken cancellationToken = default);

    ValueTask<ProjectSceneResult> AddSceneAsync(SceneCreateOptions options, CancellationToken cancellationToken = default);
}

public sealed class FileProjectSessionGateway(
    FileSessionSource fileSessions,
    AgentSessionManager sessions,
    IWorkspaceGuard workspace) : IProjectSessionGateway
{
    public ValueTask<ProjectSessionResult> OpenProjectAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        // Opening a file-backed project makes it the active writable session, so confine it to the
        // workspace here (ResolveForWrite follows symlinks, so a link out of the workspace is
        // rejected rather than silently written to on the next save).
        string resolved = workspace.ResolveForWrite(fullPath);
        FileEditingSession session = fileSessions.OpenProject(resolved);
        sessions.UseSource(fileSessions);
        return ValueTask.FromResult(new ProjectSessionResult(session, session.Project));
    }

    public ValueTask<ProjectSessionResult> CreateProjectAsync(ProjectCreateOptions options, CancellationToken cancellationToken = default)
    {
        FileEditingSession session = fileSessions.CreateProject(options);
        sessions.UseSource(fileSessions);
        session.Save(skipConflictCheck: true);
        return ValueTask.FromResult(new ProjectSessionResult(session, session.Project));
    }

    public ValueTask<ProjectSceneResult> AddSceneAsync(SceneCreateOptions options, CancellationToken cancellationToken = default)
    {
        FileEditingSession session = fileSessions.CurrentFileSession
            ?? throw new InvalidOperationException("No file editing session is open.");
        Scene scene = null!;
        // Route the add and active-scene switch through the session dispatcher so a concurrent apply_edit
        // cannot observe Project.Items mid-mutation or race SetActiveScene's recording-pipeline swap.
        session.Invoke(() =>
        {
            scene = fileSessions.AddScene(options);
            session.SetActiveScene(scene);
        });
        return ValueTask.FromResult(new ProjectSceneResult(scene, session.Project, session));
    }
}
