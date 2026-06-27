using Beutl.AgentToolkit.Documents;
using Beutl.Editor;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Sessions;

public sealed class LiveSessionSource : ISessionSource
{
    private LiveEditingSession? _currentSession;

    public EditingSessionSource Source => EditingSessionSource.LiveEditor;

    public IEditingSession? CurrentSession => _currentSession is { IsAlive: true } ? _currentSession : null;

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

    public void Invoke(Action action)
    {
        if (!IsAlive)
        {
            throw new SessionUnavailableException();
        }

        _binding.Invoke(action);
    }
}
