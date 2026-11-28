using System.Text.Json.Nodes;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

// The application holds each loaded project as a child, and its active project is one of them.
[TestFixture]
public class BeutlApplicationProjectsTests
{
    // A project file that an older build saved last, below the floor this build writes.
    private const string OlderAppVersion = "1.1.0";
    private const string OlderMinAppVersion = "1.0.0-preview.9";

    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"beutl-app-projects-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public void Every_loaded_project_is_a_child_of_the_application()
    {
        var first = new Project { Items = { new Scene() } };
        var second = new Project { Items = { new Scene() } };
        var app = new BeutlApplication { Projects = { first, second } };

        Assert.Multiple(() =>
        {
            Assert.That(first.HierarchicalParent, Is.SameAs(app));
            Assert.That(second.HierarchicalParent, Is.SameAs(app));
            Assert.That(first.Items[0].FindHierarchicalRoot(), Is.SameAs(app));
            Assert.That(second.Items[0].FindHierarchicalRoot(), Is.SameAs(app));
            Assert.That(second.Items[0].FindHierarchicalParent<Project>(), Is.SameAs(second));
            Assert.That(app.ActiveProject.Value, Is.Null);
        });

        app.Projects.Remove(first);

        Assert.Multiple(() =>
        {
            Assert.That(first.HierarchicalParent, Is.Null);
            Assert.That(first.Items[0].FindHierarchicalRoot(), Is.Null);
            Assert.That(second.HierarchicalParent, Is.SameAs(app));
        });
    }

    [Test]
    public void Switching_the_active_project_leaves_every_project_where_it_is()
    {
        var first = new Project();
        var second = new Project();
        var app = new BeutlApplication { Projects = { first, second } };

        app.ActiveProject.Value = first;
        app.ActiveProject.Value = second;

        Assert.Multiple(() =>
        {
            Assert.That(app.ActiveProject.Value, Is.SameAs(second));
            Assert.That(app.Projects, Is.EqualTo(new[] { first, second }));
            Assert.That(first.HierarchicalParent, Is.SameAs(app));
            Assert.That(second.HierarchicalParent, Is.SameAs(app));
        });

        app.ActiveProject.Value = null;

        Assert.That(second.HierarchicalParent, Is.SameAs(app));
    }

    [Test]
    public void Only_a_loaded_project_can_be_active()
    {
        var loaded = new Project();
        var app = new BeutlApplication { Projects = { loaded } };
        app.ActiveProject.Value = loaded;

        Assert.Throws<InvalidOperationException>(() => app.ActiveProject.Value = new Project());
        Assert.That(app.ActiveProject.Value, Is.SameAs(loaded));
    }

    [Test]
    public void Unloading_the_active_project_leaves_none_active()
    {
        var active = new Project();
        var other = new Project();
        var app = new BeutlApplication { Projects = { active, other } };
        app.ActiveProject.Value = active;

        app.Projects.Remove(active);

        Assert.That(app.ActiveProject.Value, Is.Null);
    }

    // Each scene finds its own project file to raise, whichever of the two projects is active.
    [Test]
    public void A_scene_saved_on_its_own_raises_the_file_of_its_own_project()
    {
        string firstProjectPath = SaveProject("first");
        string secondProjectPath = SaveProject("second");
        Project first = CoreSerializer.RestoreFromUri<Project>(new Uri(firstProjectPath));
        Project second = CoreSerializer.RestoreFromUri<Project>(new Uri(secondProjectPath));
        var app = new BeutlApplication { Projects = { first, second } };

        app.ActiveProject.Value = first;
        string firstBefore = File.ReadAllText(firstProjectPath);
        SaveScene(second);

        Assert.Multiple(() =>
        {
            Assert.That((string?)Read(secondProjectPath)["minAppVersion"], Is.EqualTo(FileFormat.Floor));
            Assert.That((string?)Read(secondProjectPath)["appVersion"], Is.EqualTo(BeutlApplication.Version));
            Assert.That(File.ReadAllText(firstProjectPath), Is.EqualTo(firstBefore));
        });

        app.ActiveProject.Value = second;
        string secondBefore = File.ReadAllText(secondProjectPath);
        SaveScene(first);

        Assert.Multiple(() =>
        {
            Assert.That((string?)Read(firstProjectPath)["minAppVersion"], Is.EqualTo(FileFormat.Floor));
            Assert.That((string?)Read(firstProjectPath)["appVersion"], Is.EqualTo(BeutlApplication.Version));
            Assert.That(File.ReadAllText(secondProjectPath), Is.EqualTo(secondBefore));
        });
    }

    private static void SaveScene(Project project)
    {
        var scene = (Scene)project.Items.Single();
        scene.Name.Value = "Edited";
        CoreSerializer.StoreToUri(scene, scene.Uri!, CoreSerializationMode.Write);
    }

    // Saves a project with one scene, then marks its project file as last saved by an older build.
    private string SaveProject(string name)
    {
        string projectPath = Path.Combine(_directory, name, $"{name}.bep");
        var project = new Project { Uri = new Uri(projectPath) };
        project.Items.Add(new Scene { Uri = new Uri(Path.Combine(_directory, name, "scene", "scene.scene")) });
        CoreSerializer.StoreToUri(project, project.Uri);

        JsonObject json = Read(projectPath);
        json["appVersion"] = OlderAppVersion;
        json["minAppVersion"] = OlderMinAppVersion;
        File.WriteAllText(projectPath, json.ToJsonString());
        return projectPath;
    }

    private static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
}
