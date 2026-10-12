using Beutl.AgentToolkit.Sessions;

namespace Beutl.AgentToolkit.Tests.Helpers;

internal static class TestWorkspace
{
    // Creates a project session and makes it current, without the first save the gateway does.
    public static FileEditingSession CreateFileSession(FileSessionSource source, ProjectCreateOptions options)
    {
        FileEditingSession session = source.CreateProjectSession(options);
        source.MakeCurrent(session);
        return session;
    }
}
