using Beutl.AgentToolkit.Documents;
using Beutl.Editor;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Sessions;

public sealed class FileEditingSession : IEditingSession, IEditingSessionDispatcher, IDisposable
{
    private RecordingPipeline _recording;
    // Attach-driven engine invariants (TimeRange/ZIndex mirroring, animation parent
    // capture) require an IHierarchicalRoot, which headless sessions otherwise lack.
    private readonly BeutlApplication _hierarchyRoot = new();
    private DateTime _projectLastWriteUtc;

    internal FileEditingSession(string sessionId, Project project, Scene scene, DateTime projectLastWriteUtc)
    {
        SessionId = sessionId;
        Project = project;
        Scene = scene;
        _hierarchyRoot.Project = project;
        _projectLastWriteUtc = projectLastWriteUtc;
        _recording = RecordingPipeline.Create(scene);
    }

    public string SessionId { get; }

    public EditingSessionSource Source => EditingSessionSource.File;

    public Project Project { get; }

    public Scene Scene { get; private set; }

    public CoreObject Root => Scene;

    public HistoryManager History => _recording.History;

    public DocumentAdapter Documents { get; } = new();

    public bool IsDirty { get; internal set; }

    public DateTime ProjectLastWriteUtc => _projectLastWriteUtc;

    public void SetActiveScene(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (ReferenceEquals(Scene, scene))
        {
            return;
        }

        if (!Project.Items.Contains(scene))
        {
            throw new InvalidOperationException("The scene does not belong to this session's project.");
        }

        // A session's undo/redo pipeline is bound to one scene, so switching scenes rebuilds it for
        // the new scene (each scene carries its own edit history).
        _recording.Dispose();
        Scene = scene;
        _recording = RecordingPipeline.Create(scene);
    }

    public void Save()
    {
        Save(skipConflictCheck: false);
    }

    public void Save(bool skipConflictCheck)
    {
        if (Project.Uri is null)
        {
            throw new InvalidOperationException("Project must have a Uri before it can be saved.");
        }

        string projectPath = Project.Uri.LocalPath;
        DateTime currentStamp = File.Exists(projectPath)
            ? File.GetLastWriteTimeUtc(projectPath)
            : DateTime.MinValue;
        if (!skipConflictCheck && currentStamp != _projectLastWriteUtc)
        {
            throw new ProjectConflictException(projectPath);
        }

        ProjectOperations.Save(Project);
        _projectLastWriteUtc = File.GetLastWriteTimeUtc(projectPath);
        IsDirty = false;
    }

    internal void AcceptExternalStamp()
    {
        // A not-yet-written target has no prior stamp; leaving the previous file's stamp here would
        // make the next Save see a spurious conflict (MinValue on disk vs the old timestamp).
        _projectLastWriteUtc = Project.Uri is { } uri && File.Exists(uri.LocalPath)
            ? File.GetLastWriteTimeUtc(uri.LocalPath)
            : DateTime.MinValue;
    }

    public void SetProjectPath(string projectPath)
    {
        string fullPath = Path.GetFullPath(projectPath);
        Project.Uri = new Uri(fullPath);
        string projectDirectory = Path.GetDirectoryName(fullPath)!;
        string projectName = Path.GetFileNameWithoutExtension(fullPath);
        // Save As must produce an independent copy. Rehome the scene/element sidecars under a
        // directory unique to the new project (its file name); regenerating from the scene name
        // alone would collide with — and overwrite — the source project's .scene/.belm files when
        // both projects live in the same folder.
        foreach (Scene scene in Project.Items.OfType<Scene>())
        {
            string sceneName = string.IsNullOrWhiteSpace(scene.Name) ? "Scene" : scene.Name;
            scene.Uri = new Uri(Path.Combine(
                projectDirectory, projectName, sceneName, $"{sceneName}.{EditorConstants.SceneFileExtension}"));
            foreach (Element element in scene.Children)
            {
                element.Uri = null;
            }
        }

        AcceptExternalStamp();
    }

    public void MarkDirty()
    {
        IsDirty = true;
    }

    public void Invoke(Action action)
    {
        action();
    }

    public void Dispose()
    {
        _recording.Dispose();
        _hierarchyRoot.Project = null;
    }
}
