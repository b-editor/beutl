using Beutl.AgentToolkit.Documents;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.Editor;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Helpers;

internal sealed class AgentToolkitTestSession : IEditingSession, IDisposable
{
    private readonly RecordingPipeline _recording;

    public AgentToolkitTestSession(CoreObject root, EditingSessionSource source = EditingSessionSource.File)
    {
        if (root is Scene { Uri: null } scene)
        {
            scene.Uri = new Uri(Path.Combine(TestWorkspace.CreateDirectory(), "Scene.scene"));
        }

        Root = root;
        Source = source;
        _recording = RecordingPipeline.Create(root);
    }

    public string SessionId { get; } = Guid.NewGuid().ToString("N");

    public EditingSessionSource Source { get; }

    public CoreObject Root { get; }

    public HistoryManager History => _recording.History;

    public DocumentAdapter Documents { get; } = new();

    public bool IsDirty => false;

    public void Invoke(Action action) => action();

    public AgentSessionManager CreateManager()
    {
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(this));
        return manager;
    }

    public EditTools CreateEditTools() => new(CreateManager());

    public void Dispose()
    {
        _recording.Dispose();
    }
}

internal sealed class AgentToolkitTestSessionSource(IEditingSession session) : ISessionSource
{
    public IEditingSession? CurrentSession => session;
}
