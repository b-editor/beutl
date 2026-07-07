using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
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

    public EditingSessionSource Source => EditingSessionSource.File;

    public IEditingSession? CurrentSession => _currentSession;

    public FileEditingSession? CurrentFileSession => _currentSession;

    public FileEditingSession OpenProject(string projectPath)
    {
        string fullPath = Path.GetFullPath(projectPath);
        if (ReadSchemaVersion(fullPath) is { } schemaVersion)
        {
            SchemaVersion.EnsureKnown(schemaVersion);
        }

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

    public FileEditingSession CreateProject(ProjectCreateOptions options)
    {
        Project project = ProjectOperations.CreateProject(options);
        Scene scene = project.Items.OfType<Scene>().First();
        var session = new FileEditingSession(Guid.NewGuid().ToString("N"), project, scene, DateTime.MinValue);
        session.MarkDirty();
        SetCurrent(session);
        return session;
    }

    // Takes the caller's session instead of re-reading the current one: a concurrent
    // open_project/create_project swap between the caller's session lookup and this call must not
    // add the scene to the newly opened project.
    public Scene AddScene(FileEditingSession session, SceneCreateOptions options)
    {
        ArgumentNullException.ThrowIfNull(session);

        Scene scene = ProjectOperations.AddScene(session.Project, options);
        session.MarkDirty();
        return scene;
    }

    public void Dispose()
    {
        FileEditingSession? current;
        lock (_swapLock)
        {
            _disposed = true;
            current = _currentSession;
            _currentSession = null;
        }

        current?.Dispose();
    }

    private void SetCurrent(FileEditingSession session)
    {
        FileEditingSession? previous;
        lock (_swapLock)
        {
            // A swap racing Dispose must not leave the incoming session undisposed and orphaned.
            if (_disposed)
            {
                session.Dispose();
                throw new ObjectDisposedException(nameof(FileSessionSource));
            }

            previous = _currentSession;
            _currentSession = session;
        }

        // Dispose outside _swapLock: Dispose quiesces on the session's dispatch lock and must not
        // stall other swaps behind an in-flight edit.
        previous?.Dispose();
    }

    private static string? ReadSchemaVersion(string path)
    {
        using FileStream stream = File.OpenRead(path);
        JsonNode? node = JsonNode.Parse(stream);
        return node is JsonObject obj && obj.TryGetPropertyValue(SchemaVersion.PropertyName, out JsonNode? version)
            ? version?.GetValue<string>()
            : null;
    }

    private static Uri CreateFileUri(string path)
    {
        return new Uri(Path.GetFullPath(path));
    }
}
