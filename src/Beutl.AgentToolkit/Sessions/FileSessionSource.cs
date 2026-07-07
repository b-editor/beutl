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

    public Scene AddScene(SceneCreateOptions options)
    {
        FileEditingSession session = _currentSession
            ?? throw new InvalidOperationException("No file editing session is open.");

        Scene scene = ProjectOperations.AddScene(session.Project, options);
        session.MarkDirty();
        return scene;
    }

    public void SaveProject()
    {
        FileEditingSession session = _currentSession
            ?? throw new InvalidOperationException("No file editing session is open.");

        session.Save();
    }

    public void Dispose()
    {
        FileEditingSession? current;
        lock (_swapLock)
        {
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
