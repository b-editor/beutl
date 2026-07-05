using Beutl.AgentToolkit.Sessions;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Sessions;

public sealed class FileEditingSessionTests
{
    [Test]
    public void SetProjectPath_disambiguates_sidecars_for_scenes_sharing_a_name()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var source = new FileSessionSource();
        FileEditingSession session = source.CreateProject(new ProjectCreateOptions(
            Path.Combine(root, "demo.bep"), 640, 360, 30, TimeSpan.FromSeconds(2), Name: "demo"));

        // A second scene sharing the seed scene's name ("demo") is what Save As must not collapse onto
        // the same <name>/<name>.scene sidecar.
        ProjectOperations.AddScene(session.Project, new SceneCreateOptions(
            640, 360, TimeSpan.Zero, TimeSpan.FromSeconds(2), Name: "demo"));

        session.SetProjectPath(Path.Combine(root, "copy.bep"));

        string[] sceneDirs = session.Project.Items.OfType<Scene>()
            .Select(s => Path.GetDirectoryName(s.Uri!.LocalPath)!)
            .ToArray();

        Assert.That(sceneDirs, Is.Unique);
    }
}
