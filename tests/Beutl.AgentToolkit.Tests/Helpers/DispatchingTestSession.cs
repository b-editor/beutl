using Beutl.AgentToolkit.Documents;
using Beutl.AgentToolkit.Sessions;
using Beutl.Editor;

namespace Beutl.AgentToolkit.Tests.Helpers;

// A live editor session that counts its dispatches. It can run a hook after the next Invoke, and it can
// reject Root reads made outside a dispatch.
internal sealed class DispatchingTestSession(CoreObject root, bool rootOnlyInsideDispatch = false)
    : IEditingSession, IDisposable
{
    private readonly AgentToolkitTestSession _inner = new(root, EditingSessionSource.LiveEditor);
    private bool _insideDispatch;

    public int InvokeCount { get; private set; }

    public Action? AfterNextInvoke { get; set; }

    public string SessionId => _inner.SessionId;

    public EditingSessionSource Source => EditingSessionSource.LiveEditor;

    public CoreObject Root => !rootOnlyInsideDispatch || _insideDispatch
        ? _inner.Root
        : throw new InvalidOperationException("Live scene read escaped the session dispatcher.");

    public HistoryManager History => _inner.History;

    public DocumentAdapter Documents => _inner.Documents;

    public bool IsDirty => false;

    public void Invoke(Action action)
    {
        InvokeCount++;
        _insideDispatch = true;
        try
        {
            action();
        }
        finally
        {
            _insideDispatch = false;
        }

        Action? after = AfterNextInvoke;
        AfterNextInvoke = null;
        after?.Invoke();
    }

    public void Dispose() => _inner.Dispose();
}
