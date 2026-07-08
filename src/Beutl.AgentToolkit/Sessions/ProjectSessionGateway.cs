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

    // activeSession is the session SessionTools already validated for this request; the gateway must
    // operate on it rather than re-reading the current session, so a concurrent open/create swap
    // cannot retarget add_scene to a different project.
    ValueTask<ProjectSceneResult> AddSceneAsync(IEditingSession activeSession, SceneCreateOptions options, CancellationToken cancellationToken = default);
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
        FileEditingSession session = fileSessions.CreateProjectSession(options);
        try
        {
            session.Save(skipConflictCheck: true);
        }
        catch
        {
            // The target could not be written; the previous session must stay current instead of
            // having been swapped out and disposed for a project that never reached disk.
            session.Dispose();
            throw;
        }

        fileSessions.MakeCurrent(session);
        sessions.UseSource(fileSessions);
        return ValueTask.FromResult(new ProjectSessionResult(session, session.Project));
    }

    public ValueTask<ProjectSceneResult> AddSceneAsync(IEditingSession activeSession, SceneCreateOptions options, CancellationToken cancellationToken = default)
    {
        if (activeSession is not FileEditingSession session)
        {
            throw new InvalidOperationException("add_scene requires a file editing session.");
        }

        Scene scene = null!;
        // Route the add and active-scene switch through the session dispatcher so a concurrent apply_edit
        // cannot observe Project.Items mid-mutation or race SetActiveScene's recording-pipeline swap.
        session.Invoke(() =>
        {
            scene = fileSessions.AddScene(session, options);
            session.SetActiveScene(scene);
        });
        return ValueTask.FromResult(new ProjectSceneResult(scene, session.Project, session));
    }
}
