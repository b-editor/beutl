using Beutl.AgentToolkit.Documents;
using Beutl.AgentToolkit.Sessions;
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
            string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            scene.Uri = new Uri(Path.Combine(dir, "Scene.scene"));
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

    public bool IsDirty { get; private set; }

    public void Dispose()
    {
        _recording.Dispose();
    }
}

internal sealed class AgentToolkitTestSessionSource(IEditingSession session) : ISessionSource
{
    public EditingSessionSource Source => session.Source;

    public IEditingSession? CurrentSession => session;
}
