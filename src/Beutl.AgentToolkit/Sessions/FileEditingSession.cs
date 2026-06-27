using Beutl.AgentToolkit.Documents;
using Beutl.Editor;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Sessions;

public sealed class FileEditingSession : IEditingSession, IEditingSessionDispatcher, IDisposable
{
    private readonly RecordingPipeline _recording;
    private DateTime _projectLastWriteUtc;

    internal FileEditingSession(string sessionId, Project project, Scene scene, DateTime projectLastWriteUtc)
    {
        SessionId = sessionId;
        Project = project;
        Scene = scene;
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
        if (!ReferenceEquals(Scene, scene))
        {
            throw new NotSupportedException("Switching the active scene is not supported by this session yet.");
        }
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
        if (Project.Uri is { } uri && File.Exists(uri.LocalPath))
        {
            _projectLastWriteUtc = File.GetLastWriteTimeUtc(uri.LocalPath);
        }
    }

    public void SetProjectPath(string projectPath)
    {
        Project.Uri = new Uri(Path.GetFullPath(projectPath));
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
    }
}
