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

    [Test]
    public void SetProjectPath_does_not_overwrite_existing_sidecar_file()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string existingDir = Path.Combine(root, "copy", "demo");
        Directory.CreateDirectory(existingDir);
        string existingSidecar = Path.Combine(existingDir, "demo.scene");
        File.WriteAllText(existingSidecar, "existing sidecar");
        using var source = new FileSessionSource();
        FileEditingSession session = source.CreateProject(new ProjectCreateOptions(
            Path.Combine(root, "demo.bep"), 640, 360, 30, TimeSpan.FromSeconds(2), Name: "demo"));

        session.SetProjectPath(Path.Combine(root, "copy.bep"));
        session.Save(skipConflictCheck: true);

        Scene scene = session.Project.Items.OfType<Scene>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(existingSidecar), Is.EqualTo("existing sidecar"));
            Assert.That(scene.Uri!.LocalPath, Is.Not.EqualTo(existingSidecar));
            Assert.That(File.Exists(scene.Uri.LocalPath), Is.True);
            Assert.That(Path.GetFileName(Path.GetDirectoryName(scene.Uri.LocalPath)!), Is.EqualTo("demo-2"));
        });
    }
}
