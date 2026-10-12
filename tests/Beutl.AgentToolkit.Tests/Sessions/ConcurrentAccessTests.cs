using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;

namespace Beutl.AgentToolkit.Tests.Sessions;

public sealed class ConcurrentAccessTests
{
    [Test]
    public void Save_detects_project_modified_after_open()
    {
        string root = TestWorkspace.CreateDirectory();
        string projectPath = Path.Combine(root, "conflict.bep");
        using var source = new FileSessionSource();
        FileEditingSession session = TestWorkspace.CreateFileSession(source, new ProjectCreateOptions(
            projectPath,
            320,
            180,
            30,
            TimeSpan.FromSeconds(1),
            Name: "conflict"));
        session.Save(skipConflictCheck: true);

        File.SetLastWriteTimeUtc(projectPath, DateTime.UtcNow.AddSeconds(10));

        Assert.Throws<ProjectConflictException>(() => session.Save(skipConflictCheck: false));
    }
}
