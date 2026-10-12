using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Sessions;

public sealed class FileSessionSource : ISessionSource, IDisposable
{
    // Concurrent open_project/create_project calls race on the current-session swap; each caller
    // must get back the session IT created (never the shared field, which another call may have
    // replaced), and the replaced session must be disposed exactly once.
    private readonly object _swapLock = new();
    private volatile FileEditingSession? _currentSession;
    private bool _disposed;

    public IEditingSession? CurrentSession => _currentSession;

    public FileEditingSession OpenProject(string projectPath)
    {
        string fullPath = Path.GetFullPath(projectPath);
        var project = CoreSerializer.RestoreFromUri<Project>(CreateFileUri(fullPath));
        Scene scene = project.Items.OfType<Scene>().FirstOrDefault()
                      ?? throw new InvalidOperationException("The project does not contain a scene.");

        var session = new FileEditingSession(
            Guid.NewGuid().ToString("N"),
            project,
            scene,
            File.Exists(fullPath) ? File.GetLastWriteTimeUtc(fullPath) : DateTime.MinValue);
        SetCurrent(session);
        return session;
    }

    // Pure factory (no publish): lets the gateway persist the project first, so a failed initial
    // save does not swap out — and dispose — the previously active session.
    public FileEditingSession CreateProjectSession(ProjectCreateOptions options)
    {
        Project project = ProjectOperations.CreateProject(options);
        Scene scene = project.Items.OfType<Scene>().First();
        var session = new FileEditingSession(Guid.NewGuid().ToString("N"), project, scene, DateTime.MinValue);
        session.MarkDirty();
        return session;
    }

    public void MakeCurrent(FileEditingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        SetCurrent(session);
    }

    public void Dispose()
    {
        FileEditingSession? current;
        lock (_swapLock)
        {
            _disposed = true;
            current = _currentSession;
            _currentSession = null;
            if (current is not null)
                Media.FontManager.Instance.ClearProjectFonts(current.Project);
        }

        current?.Dispose();
    }

    private void SetCurrent(FileEditingSession session)
    {
        FileEditingSession? previous;
        bool disposedRace;
        lock (_swapLock)
        {
            disposedRace = _disposed;
            previous = disposedRace ? null : _currentSession;
            if (!disposedRace)
            {
                Media.FontManager.Instance.LoadProjectFonts(session.Project);
                _currentSession = session;
            }
        }

        // Dispose outside _swapLock: Dispose quiesces on the session's dispatch lock and must not
        // stall other swaps behind an in-flight edit.
        if (disposedRace)
        {
            // A swap racing Dispose must not leave the incoming session undisposed and orphaned.
            session.Dispose();
            throw new ObjectDisposedException(nameof(FileSessionSource));
        }

        previous?.Dispose();
    }

    private static Uri CreateFileUri(string path)
    {
        return new Uri(Path.GetFullPath(path));
    }
}
