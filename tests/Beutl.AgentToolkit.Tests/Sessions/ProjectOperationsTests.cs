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

    // Two scenes carrying the same in-project sidecar Uri would overwrite each other on save; Save must
    // null the duplicate so the Ensure* helper regenerates it on a distinct path.
    [Test]
    public void Save_NullsDuplicateSidecarUris_RegeneratesDistinctPaths()
    {
        Project project = ProjectOperations.CreateProject(new ProjectCreateOptions(
            Path.Combine(_tempRoot, "proj.bep"),
            Width: 1920,
            Height: 1080,
            FrameRate: 30,
            Duration: TimeSpan.FromSeconds(10)));

        Scene first = project.Items.OfType<Scene>().First();
        Scene second = ProjectOperations.AddScene(project, new SceneCreateOptions(
            Width: 1920,
            Height: 1080,
            Start: TimeSpan.Zero,
            Duration: TimeSpan.FromSeconds(5),
            Name: "other"));

        second.Uri = first.Uri;

        ProjectOperations.Save(project);

        Assert.That(second.Uri, Is.Not.EqualTo(first.Uri), "Duplicate sidecar Uri must be regenerated on a distinct path.");
        Assert.That(
            Path.GetDirectoryName(second.Uri!.LocalPath),
            Is.Not.EqualTo(Path.GetDirectoryName(first.Uri!.LocalPath)),
            "Regenerated sidecar must live in its own directory.");
    }

    [Test]
    public void Save_RehomesSceneSidecarThroughInProjectSymlink_RegeneratesInsideProject()
    {
        Project project = ProjectOperations.CreateProject(new ProjectCreateOptions(
            Path.Combine(_tempRoot, "proj.bep"),
            Width: 1920,
            Height: 1080,
            FrameRate: 30,
            Duration: TimeSpan.FromSeconds(10)));

        string projectDir = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        string outsideDir = Path.Combine(Path.GetTempPath(), "po-escape-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideDir);
        try
        {
            string link = Path.Combine(projectDir, "link");
            CreateDirectorySymlinkOrIgnore(link, outsideDir);

            Scene scene = project.Items.OfType<Scene>().First();
            // A textual boundary check would accept this in-project path; the symlink actually redirects
            // the write outside the project tree.
            scene.Uri = new Uri(Path.Combine(link, "escape.scene"));

            ProjectOperations.Save(project);

            string regenerated = PathBoundary.ResolveDeepestExistingTarget(scene.Uri!.LocalPath);
            Assert.That(
                regenerated.StartsWith(projectDir, PathComparison.ForCurrentPlatform),
                Is.True,
                $"Scene sidecar must be regenerated inside the project directory, got: {regenerated}");
            Assert.That(Directory.EnumerateFileSystemEntries(outsideDir), Is.Empty);
        }
        finally
        {
            Directory.Delete(outsideDir, true);
        }
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

    private static void CreateDirectorySymlinkOrIgnore(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Ignore($"Symbolic links are not creatable in this environment: {ex.Message}");
        }
    }

    [TestCase("Intro")]
    [TestCase("scene-2")]
    [TestCase("Shot 01")]
    public void IsValidSceneName_AcceptsSinglePathSegment(string name)
    {
        Assert.That(ProjectOperations.IsValidSceneName(name), Is.True);
    }

    [Test]
    public void NormalizeSidecarUrisWithinProject_RehomesOutOfProjectUris_WithoutTouchingTheFilesystem()
    {
        Project project = ProjectOperations.CreateProject(new ProjectCreateOptions(
            Path.Combine(_tempRoot, "proj.bep"),
            Width: 1920,
            Height: 1080,
            FrameRate: 30,
            Duration: TimeSpan.FromSeconds(10)));

        string projectDir = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        Scene scene = project.Items.OfType<Scene>().First();

        // Simulate apply_edit against a hand-edited project whose scene and element sidecars both
        // escape the project tree.
        string outsideDir = Path.Combine(Path.GetTempPath(), "escape-" + Guid.NewGuid().ToString("N"));
        scene.Uri = new Uri(Path.Combine(outsideDir, "escape.scene"));
        var element = new Element
        {
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(Path.Combine(outsideDir, "escape.belm"))
        };
        scene.Children.Add(element);

        ProjectOperations.NormalizeSidecarUrisWithinProject(scene);

        string rehomedSceneDir = Path.GetDirectoryName(scene.Uri!.LocalPath)!;
        Assert.Multiple(() =>
        {
            Assert.That(
                Path.GetFullPath(scene.Uri!.LocalPath).StartsWith(projectDir, PathComparison.ForCurrentPlatform),
                Is.True,
                $"Scene sidecar must be rehomed inside the project, got: {scene.Uri.LocalPath}");
            Assert.That(element.Uri, Is.Not.Null);
            Assert.That(
                Path.GetFullPath(element.Uri!.LocalPath).StartsWith(projectDir, PathComparison.ForCurrentPlatform),
                Is.True,
                $"Element sidecar must be inside the project, got: {element.Uri!.LocalPath}");
            Assert.That(Directory.Exists(outsideDir), Is.False, "No out-of-project directory may be created.");
            Assert.That(Directory.Exists(rehomedSceneDir), Is.False, "URI normalization must not create directories.");
        });
    }

    [Test]
    public void NormalizeSidecarUrisWithinProject_DoesNotThrow_WhenSceneHasNoUri()
    {
        // A live/unsaved scene not attached to a project has no Uri to home sidecars into; without the
        // guard AssignMissingElementUris throws InvalidOperationException on the null Uri.
        var scene = new Scene(1920, 1080, "Scene");

        Assert.DoesNotThrow(() => ProjectOperations.NormalizeSidecarUrisWithinProject(scene));
    }
}
