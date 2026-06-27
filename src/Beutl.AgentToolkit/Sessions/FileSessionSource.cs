using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Sessions;

public sealed class FileSessionSource : ISessionSource, IDisposable
{
    private FileEditingSession? _currentSession;

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

        SetCurrent(new FileEditingSession(
            Guid.NewGuid().ToString("N"),
            project,
            scene,
            File.Exists(fullPath) ? File.GetLastWriteTimeUtc(fullPath) : DateTime.MinValue));
        return _currentSession!;
    }

    public FileEditingSession CreateProject(ProjectCreateOptions options)
    {
        Project project = ProjectOperations.CreateProject(options);
        Scene scene = project.Items.OfType<Scene>().First();
        SetCurrent(new FileEditingSession(Guid.NewGuid().ToString("N"), project, scene, DateTime.MinValue));
        _currentSession!.MarkDirty();
        return _currentSession;
    }

    public Scene AddScene(SceneCreateOptions options)
    {
        if (_currentSession is null)
        {
            throw new InvalidOperationException("No file editing session is open.");
        }

        Scene scene = ProjectOperations.AddScene(_currentSession.Project, options);
        _currentSession.MarkDirty();
        return scene;
    }

    public void SaveProject()
    {
        if (_currentSession is null)
        {
            throw new InvalidOperationException("No file editing session is open.");
        }

        _currentSession.Save();
    }

    public void Dispose()
    {
        _currentSession?.Dispose();
    }

    private void SetCurrent(FileEditingSession session)
    {
        _currentSession?.Dispose();
        _currentSession = session;
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
