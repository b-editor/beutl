using Beutl.AgentToolkit.Common;
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
    private readonly object _dispatchLock = new();
    private DateTime _projectLastWriteUtc;
    private bool _disposed;

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

        // ProjectOperations.Save walks and writes the Project/Scene collections; take the dispatch lock so
        // a concurrent Invoke cannot persist a half-applied edit or throw while the writer enumerates them.
        lock (_dispatchLock)
        {
            if (_disposed)
            {
                throw new SessionUnavailableException();
            }

            string projectPath = Project.Uri.LocalPath;
            DateTime currentStamp = File.Exists(projectPath)
                ? File.GetLastWriteTimeUtc(projectPath)
                : DateTime.MinValue;
            if (!skipConflictCheck && currentStamp != _projectLastWriteUtc)
            {
                throw new ProjectConflictException(projectPath);
            }

            UriState original = CaptureUriState();
            try
            {
                ProjectOperations.Save(Project);
            }
            catch
            {
                // Save rehomes/nulls/assigns sidecar URIs before the fallible project write; a
                // failed save must leave the open graph exactly as it was.
                RestoreUriState(original);
                throw;
            }

            _projectLastWriteUtc = File.GetLastWriteTimeUtc(projectPath);
            IsDirty = false;
        }
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
        // Rewriting the project/scene/element URIs is part of the same atomic critical section as Save;
        // hold the dispatch lock so a concurrent apply_edit/add_scene cannot observe a half-rehomed graph.
        lock (_dispatchLock)
        {
            SetProjectPathCore(projectPath);
        }
    }

    private void SetProjectPathCore(string projectPath)
    {
        string fullPath = Path.GetFullPath(projectPath);
        Project.Uri = new Uri(fullPath);
        string projectDirectory = Path.GetDirectoryName(fullPath)!;
        string projectName = Path.GetFileNameWithoutExtension(fullPath);
        // Save As must produce an independent copy. Rehome the scene/element sidecars under a
        // directory unique to the new project (its file name); regenerating from the scene name
        // alone would collide with — and overwrite — the source project's .scene/.belm files when
        // both projects live in the same folder.
        var usedDirs = new HashSet<string>(StringComparer.FromComparison(PathComparison.ForCurrentPlatform));
        int index = 1;
        foreach (Scene scene in Project.Items.OfType<Scene>())
        {
            // A project loaded from disk can carry a scene name that was never validated (e.g.
            // "../../outside"); fall back to a safe segment so the rehomed sidecar stays under the
            // new project directory.
            string sceneName = ProjectOperations.IsValidSceneName(scene.Name) ? scene.Name : $"Scene{index}";

            string scenePath = ProjectOperations.ReserveUniqueScenePath(
                Path.Combine(projectDirectory, projectName),
                sceneName,
                usedDirs);
            scene.Uri = new Uri(scenePath);
            foreach (Element element in scene.Children)
            {
                element.Uri = null;
            }

            index++;
        }

        AcceptExternalStamp();
    }

    // Save As rehomes the project/scene/element URIs and then writes; run capture → rehome → save —
    // and the restore on failure — as one critical section so a concurrent Invoke cannot mutate the
    // graph between the steps or observe a half-captured/half-restored URI set.
    internal void SaveAs(string projectPath, bool skipConflictCheck)
    {
        lock (_dispatchLock)
        {
            if (_disposed)
            {
                throw new SessionUnavailableException();
            }

            UriState original = CaptureUriState();
            SetProjectPathCore(projectPath);
            try
            {
                Save(skipConflictCheck);
            }
            catch
            {
                // A failed Save As would leave the session pointed at the new location with sidecar
                // URIs rewritten; restore the originals so a later plain save still targets the source.
                RestoreUriState(original);
                throw;
            }
        }
    }

    private UriState CaptureUriState()
    {
        return new UriState(ProjectOperations.CaptureUriState(Project), _projectLastWriteUtc);
    }

    private void RestoreUriState(UriState state)
    {
        ProjectOperations.RestoreUriState(Project, state.Uris);
        _projectLastWriteUtc = state.Stamp;
    }

    private sealed record UriState(ProjectUriState Uris, DateTime Stamp);

    public void MarkDirty()
    {
        IsDirty = true;
    }

    public void Invoke(Action action)
    {
        // The reconciler treats each Invoke as its atomic read/merge/write critical section; the live
        // session gets that from the single UI thread, so file sessions need a per-session lock to
        // keep concurrent tool calls from interleaving and dropping the earlier edit.
        lock (_dispatchLock)
        {
            // A request can obtain this session from RequireSession() and then be swapped out (Dispose)
            // before it enters here; reject the dispatch cleanly instead of running against a disposed
            // HistoryManager and surfacing an internal error.
            if (_disposed)
            {
                throw new SessionUnavailableException();
            }

            action();
        }
    }

    public void Dispose()
    {
        // Quiesce any in-flight Invoke so a concurrent session swap cannot dispose HistoryManager mid-edit.
        lock (_dispatchLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _recording.Dispose();
            _hierarchyRoot.Project = null;
        }
    }
}
