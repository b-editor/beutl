using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Sessions;

public sealed class ProjectOperationsTests
{
    private string _tempRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "project-operations-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, true);
        }
    }

    [Test]
    public void AddScene_WithNameColliding_DerivesDistinctSidecarDirectory()
    {
        Project project = ProjectOperations.CreateProject(new ProjectCreateOptions(
            Path.Combine(_tempRoot, "proj.bep"),
            Width: 1920,
            Height: 1080,
            FrameRate: 30,
            Duration: TimeSpan.FromSeconds(10)));

        // CreateProject seeds a scene named after the project ("proj") at proj/proj.scene.
        Scene added = ProjectOperations.AddScene(project, new SceneCreateOptions(
            Width: 1920,
            Height: 1080,
            Start: TimeSpan.Zero,
            Duration: TimeSpan.FromSeconds(5),
            Name: "proj"));

        Scene seeded = project.Items.OfType<Scene>().First();
        string seededDir = Path.GetDirectoryName(seeded.Uri!.LocalPath)!;
        string addedDir = Path.GetDirectoryName(added.Uri!.LocalPath)!;

        Assert.That(addedDir, Is.Not.EqualTo(seededDir));
        Assert.That(Path.GetFileName(addedDir), Is.EqualTo("proj-2"));
    }

    [Test]
    public void Save_RehomesSceneSidecarOutsideProject_RegeneratesInsideProject()
    {
        Project project = ProjectOperations.CreateProject(new ProjectCreateOptions(
            Path.Combine(_tempRoot, "proj.bep"),
            Width: 1920,
            Height: 1080,
            FrameRate: 30,
            Duration: TimeSpan.FromSeconds(10)));

        string projectDir = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        Scene scene = project.Items.OfType<Scene>().First();

        // Simulate a hand-edited/malicious project whose scene sidecar Uri escapes the project tree.
        string outside = Path.Combine(Path.GetTempPath(), "escape-" + Guid.NewGuid().ToString("N"), "escape.scene");
        scene.Uri = new Uri(outside);

        ProjectOperations.Save(project);

        string regeneratedDir = Path.GetDirectoryName(scene.Uri!.LocalPath)!;
        Assert.That(
            regeneratedDir.StartsWith(projectDir, PathComparison.ForCurrentPlatform),
            Is.True,
            $"Scene sidecar must be regenerated inside the project directory, got: {scene.Uri.LocalPath}");
        Assert.That(File.Exists(outside), Is.False, "The out-of-project sidecar must not be written.");
    }

    [TestCase("..")]
    [TestCase(".")]
    [TestCase("a/b")]
    [TestCase("")]
    [TestCase("   ")]
    public void IsValidSceneName_RejectsTraversalAndSeparators(string name)
    {
        Assert.That(ProjectOperations.IsValidSceneName(name), Is.False);
    }

    [TestCase("Intro")]
    [TestCase("scene-2")]
    [TestCase("Shot 01")]
    public void IsValidSceneName_AcceptsSinglePathSegment(string name)
    {
        Assert.That(ProjectOperations.IsValidSceneName(name), Is.True);
    }
}
