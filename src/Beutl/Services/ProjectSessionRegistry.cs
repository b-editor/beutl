using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Beutl.Media;
using Reactive.Bindings;

namespace Beutl.Services;

// The projects loaded into an application, one session each, and the one the editor shows.
// The registry is the only writer of the application's Projects and ActiveProject, so they always match its
// sessions. Use it on the thread that owns the projects.
internal sealed class ProjectSessionRegistry
{
    private readonly BeutlApplication _app;
    private readonly ObservableCollection<ProjectSession> _sessions = [];
    private readonly ReactivePropertySlim<ProjectSession?> _activeSession = new();

    public ProjectSessionRegistry(BeutlApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        _app = app;
        Sessions = new ReadOnlyObservableCollection<ProjectSession>(_sessions);
        ActiveSession = _activeSession.ToReadOnlyReactivePropertySlim();
        ActiveProject = _activeSession.Select(session => session?.Project).ToReadOnlyReactivePropertySlim();
    }

    // The registry of BeutlApplication.Current, which every ProjectService of the process shares.
    public static ProjectSessionRegistry Current { get; } = new(BeutlApplication.Current);

    public ReadOnlyObservableCollection<ProjectSession> Sessions { get; }

    public IReadOnlyReactiveProperty<ProjectSession?> ActiveSession { get; }

    public IReadOnlyReactiveProperty<Project?> ActiveProject { get; }

    // Loads project into the application without activating it. The project must not belong to a hierarchy yet,
    // and no loaded session may hold it or its file.
    public ProjectSession Add(Project project, FontManager projectFonts)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(projectFonts);
        if (Find(project) is not null)
            throw new InvalidOperationException("The project is already loaded.");
        if (project.HierarchicalParent is not null)
            throw new InvalidOperationException("The project already has a parent.");

        var session = new ProjectSession(project, projectFonts);
        if (session.FilePath is { } file && Find(file) is not null)
            throw new InvalidOperationException($"A project at '{file}' is already loaded.");

        _app.Projects.Add(project);
        _sessions.Add(session);
        return session;
    }

    // Makes session the one the editor shows, or shows none for null.
    public void Activate(ProjectSession? session)
    {
        if (session is not null && !_sessions.Contains(session))
            throw new InvalidOperationException("The session is not loaded.");

        // The application's project changes first, so a subscriber to ActiveSession finds both in agreement.
        _app.ActiveProject.Value = session?.Project;
        _activeSession.Value = session;
    }

    // Unloads session; the active session is deactivated first. Returns false when it was not loaded.
    public bool Remove(ProjectSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!_sessions.Contains(session))
            return false;

        if (ReferenceEquals(_activeSession.Value, session))
            Activate(null);

        _sessions.Remove(session);
        _app.Projects.Remove(session.Project);
        return true;
    }

    public ProjectSession? Find(Project project)
    {
        foreach (ProjectSession session in _sessions)
        {
            if (ReferenceEquals(session.Project, project))
                return session;
        }

        return null;
    }

    public ProjectSession? Find(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        foreach (ProjectSession session in _sessions)
        {
            if (session.IsAt(file))
                return session;
        }

        return null;
    }
}
