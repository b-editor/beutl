using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Sessions;

public sealed record ProjectSessionResult(IEditingSession Session, Project Project);

public sealed record ProjectSceneResult(Scene Scene, Project Project);

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
    AgentSessionManager sessions) : IProjectSessionGateway
{
    public ValueTask<ProjectSessionResult> OpenProjectAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        FileEditingSession session = fileSessions.OpenProject(fullPath);
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
        Scene scene = fileSessions.AddScene(options);
        return ValueTask.FromResult(new ProjectSceneResult(scene, fileSessions.CurrentFileSession!.Project));
    }
}
