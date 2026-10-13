namespace Beutl.AgentToolkit.Sessions;

public interface ISessionSource
{
    IEditingSession? CurrentSession { get; }
}
