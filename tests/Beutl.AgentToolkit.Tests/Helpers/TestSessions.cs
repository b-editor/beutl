using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;

namespace Beutl.AgentToolkit.Tests.Helpers;

internal static class TestSessions
{
    public static AgentSessionManager CreateManager(IEditingSession session)
    {
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        return manager;
    }

    public static SessionTools CreateSessionTools(FileSessionSource source, AgentSessionManager manager)
    {
        return new SessionTools(
            new FileProjectSessionGateway(source, manager),
            manager,
            new DestructiveGuard(),
            new RenderJobManager());
    }
}
