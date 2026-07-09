namespace Beutl.AgentToolkit.Sessions;

public interface ISessionSource
{
    EditingSessionSource Source { get; }

    IEditingSession? CurrentSession { get; }
}
