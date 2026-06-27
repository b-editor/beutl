using Beutl.AgentToolkit.Sessions;

namespace Beutl.AgentToolkit.Tests.Sessions;

public sealed class ConcurrentAccessTests
{
    [Test]
    public void Save_detects_project_modified_after_open()
    {
        string root = CreateWorkspace();
        string projectPath = Path.Combine(root, "conflict.bep");
        using var source = new FileSessionSource();
        FileEditingSession session = source.CreateProject(new ProjectCreateOptions(
            projectPath,
            320,
            180,
            30,
            TimeSpan.FromSeconds(1),
            Name: "conflict"));
        session.Save(skipConflictCheck: true);

        File.SetLastWriteTimeUtc(projectPath, DateTime.UtcNow.AddSeconds(10));

        Assert.Throws<ProjectConflictException>(() => session.Save());
    }

    private static string CreateWorkspace()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
