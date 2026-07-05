using Beutl.AgentToolkit.Documents;
using Beutl.Editor;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Sessions;

public sealed class LiveSessionSource : ISessionSource
{
    private LiveEditingSession? _currentSession;

    public EditingSessionSource Source => EditingSessionSource.LiveEditor;

    // IsAlive dereferences editor-owned Scene/History on the UI thread; an MCP request thread probing
    // liveness here would race a tab switch/close. Dispatch the check through the binding so editor
    // state is only touched on the editor's own thread.
    public IEditingSession? CurrentSession
    {
        get
        {
            LiveEditingSession? session = _currentSession;
            return session is not null && session.ProbeIsAlive() ? session : null;
        }
    }

    public LiveEditingSession Attach(ILiveSessionBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (!binding.IsAlive || binding.ActiveScene is null || binding.ActiveHistory is null)
        {
            throw new SessionUnavailableException();
        }

        _currentSession = new LiveEditingSession(Guid.NewGuid().ToString("N"), binding);
        return _currentSession;
    }
}

public sealed class LiveEditingSession : IEditingSession, IEditingSessionDispatcher
{
    private readonly ILiveSessionBinding _binding;

    internal LiveEditingSession(string sessionId, ILiveSessionBinding binding)
    {
        SessionId = sessionId;
        _binding = binding;
    }

    public string SessionId { get; }

    public EditingSessionSource Source => EditingSessionSource.LiveEditor;

    public CoreObject Root => _binding.ActiveScene ?? throw new SessionUnavailableException();

    public HistoryManager History => _binding.ActiveHistory ?? throw new SessionUnavailableException();

    public DocumentAdapter Documents { get; } = new();

    public bool IsDirty => false;

    public bool IsAlive => _binding.IsAlive && _binding.ActiveScene is not null && _binding.ActiveHistory is not null;

    // Run the liveness check on the editor's dispatcher (via the binding) instead of dereferencing
    // editor-owned Scene/History from the MCP request thread. LiveEditingSession.Invoke guards on
    // IsAlive and so cannot be used to probe liveness itself; reach the binding dispatcher directly.
    public bool ProbeIsAlive()
    {
        bool alive = false;
        _binding.Invoke(() => alive = _binding.IsAlive && _binding.ActiveScene is not null && _binding.ActiveHistory is not null);
        return alive;
    }

    public void Invoke(Action action)
    {
        if (!IsAlive)
        {
            throw new SessionUnavailableException();
        }

        _binding.Invoke(action);
    }
}
