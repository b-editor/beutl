namespace Beutl.AgentToolkit.Sessions;

public interface IEditingSessionDispatcher
{
    void Invoke(Action action);
}
