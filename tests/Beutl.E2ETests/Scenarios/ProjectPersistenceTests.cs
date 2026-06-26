using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.E2ETests.Scenarios;

[TestFixture]
public class ProjectPersistenceTests
{
    private string _baseDir = null!;

    [SetUp]
    public void SetUp()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), $"beutl-e2e-project_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_baseDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, recursive: true);
    }

    [Test]
    public void Project_with_scene_and_variables_round_trips_through_disk()
    {
        var projectUri = new Uri(Path.Combine(_baseDir, "demo.bproj"));
        var sceneUri = new Uri(Path.Combine(_baseDir, "scene0", "scene0.scene"));
        var elementUri = new Uri(Path.Combine(_baseDir, "scene0", "clip.belm"));
        // The recursive reference save writes each referenced file without creating its parent
        // directory, so the scene subdirectory has to exist before StoreToUri.
        Directory.CreateDirectory(Path.Combine(_baseDir, "scene0"));

        var project = new Project { Uri = projectUri };
        project.Variables[ProjectVariableKeys.FrameRate] = "30";
        project.Variables[ProjectVariableKeys.SampleRate] = "48000";

        var scene = new Scene(1920, 1080, "scene0") { Uri = sceneUri };
        var element = new Element
        {
            Start = TimeSpan.FromSeconds(2),
            Length = TimeSpan.FromSeconds(4),
            ZIndex = 1,
            Uri = elementUri,
        };
        scene.Children.Add(element);
        element.Objects.Add(new RectShape());
        project.Items.Add(scene);

        CoreSerializer.StoreToUri(project, projectUri);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(projectUri.LocalPath), Is.True);
            Assert.That(File.Exists(sceneUri.LocalPath), Is.True);
            Assert.That(File.Exists(elementUri.LocalPath), Is.True);
        });

        var restored = CoreSerializer.RestoreFromUri<Project>(projectUri);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Items, Has.Count.EqualTo(1));
            Assert.That(restored.Variables[ProjectVariableKeys.FrameRate], Is.EqualTo("30"));
            Assert.That(restored.Variables[ProjectVariableKeys.SampleRate], Is.EqualTo("48000"));
        });

        var restoredScene = (Scene)restored.Items[0];
        Assert.Multiple(() =>
        {
            Assert.That(restoredScene.FrameSize, Is.EqualTo(new PixelSize(1920, 1080)));
            Assert.That(restoredScene.Name, Is.EqualTo("scene0"));
            Assert.That(restoredScene.Children, Has.Count.EqualTo(1));
        });

        Element restoredElement = restoredScene.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(restoredElement.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(restoredElement.Length, Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(restoredElement.ZIndex, Is.EqualTo(1));
            Assert.That(restoredElement.Objects.OfType<RectShape>().Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public void Project_with_multiple_scenes_preserves_item_order_and_ids()
    {
        var projectUri = new Uri(Path.Combine(_baseDir, "multi.bproj"));
        var project = new Project { Uri = projectUri };

        var first = new Scene(800, 600, "first")
        {
            Uri = new Uri(Path.Combine(_baseDir, "a", "a.scene")),
        };
        var second = new Scene(400, 300, "second")
        {
            Uri = new Uri(Path.Combine(_baseDir, "b", "b.scene")),
        };
        // The recursive reference save writes each scene file directly without creating its
        // directory, mirroring how the app lays the project tree out on disk before saving.
        Directory.CreateDirectory(Path.Combine(_baseDir, "a"));
        Directory.CreateDirectory(Path.Combine(_baseDir, "b"));
        project.Items.Add(first);
        project.Items.Add(second);

        CoreSerializer.StoreToUri(project, projectUri);
        var restored = CoreSerializer.RestoreFromUri<Project>(projectUri);

        Assert.That(restored.Items, Has.Count.EqualTo(2));
        var restoredFirst = (Scene)restored.Items[0];
        var restoredSecond = (Scene)restored.Items[1];

        Assert.Multiple(() =>
        {
            Assert.That(restoredFirst.Id, Is.EqualTo(first.Id));
            Assert.That(restoredFirst.FrameSize, Is.EqualTo(new PixelSize(800, 600)));
            Assert.That(restoredSecond.Id, Is.EqualTo(second.Id));
            Assert.That(restoredSecond.FrameSize, Is.EqualTo(new PixelSize(400, 300)));
        });
    }

    [Test]
    public void Restored_project_persists_again_with_identical_scene_state()
    {
        var projectUri = new Uri(Path.Combine(_baseDir, "resave.bproj"));
        var sceneUri = new Uri(Path.Combine(_baseDir, "s", "s.scene"));
        var elementUri = new Uri(Path.Combine(_baseDir, "s", "e.belm"));
        Directory.CreateDirectory(Path.Combine(_baseDir, "s"));

        var project = new Project { Uri = projectUri };
        var scene = new Scene(1024, 768, "s") { Uri = sceneUri };
        scene.Children.Add(new Element { Length = TimeSpan.FromSeconds(3), Uri = elementUri });
        project.Items.Add(scene);
        CoreSerializer.StoreToUri(project, projectUri);

        var firstRestore = CoreSerializer.RestoreFromUri<Project>(projectUri);
        CoreSerializer.StoreToUri(firstRestore, projectUri);
        var secondRestore = CoreSerializer.RestoreFromUri<Project>(projectUri);

        var secondScene = (Scene)secondRestore.Items[0];
        Assert.Multiple(() =>
        {
            Assert.That(secondRestore.Items, Has.Count.EqualTo(1));
            Assert.That(secondScene.FrameSize, Is.EqualTo(new PixelSize(1024, 768)));
            Assert.That(secondScene.Children, Has.Count.EqualTo(1));
            Assert.That(secondScene.Children[0].Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
        });
    }
}
