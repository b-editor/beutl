using System.Text.Json.Nodes;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class SceneTests
{
    private string _tempDirectory = null!;

    [TestCase("a#b.belm")]
    [TestCase("50%20off.belm")]
    public void SceneReload_PreservesLiteralElementFileNames(string fileName)
    {
        var scene = new Scene { Uri = new Uri(Path.Combine(_tempDirectory, "main.scene")) };
        var element = new Element { Uri = new Uri(Path.Combine(_tempDirectory, fileName)) };
        scene.Children.Add(element);
        CoreSerializer.StoreToUri(element, element.Uri);
        CoreSerializer.StoreToUri(scene, scene.Uri);
        Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        Assert.That(restored.Children.Single().Uri!.LocalPath, Is.EqualTo(element.Uri.LocalPath));
    }

    [SetUp]
    public void SetUp()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"beutl-scene-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempDirectory, "elements"));
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_tempDirectory, recursive: true);
    }

    [Test]
    public void Storing_a_scene_persists_every_child_element_file()
    {
        string scenePath = Path.Combine(_tempDirectory, "project.scene");
        string elementPath = Path.Combine(_tempDirectory, "elements", "child.belm");
        var scene = new Scene { Uri = new Uri(scenePath) };
        scene.Children.Add(new Element { Uri = new Uri(elementPath), Name = "child" });

        CoreSerializer.StoreToUri(scene, scene.Uri!);

        Assert.That(File.Exists(elementPath), Is.True);
    }

    [Test]
    public void Element_patterns_normalize_legacy_backslashes_when_stored()
    {
        string scenePath = Path.Combine(_tempDirectory, "project.scene");
        string includedPath = Path.Combine(_tempDirectory, "elements", "included.belm");
        string excludedPath = Path.Combine(_tempDirectory, "elements", "excluded.belm");
        CoreSerializer.StoreToUri(new Element(), new Uri(includedPath));
        CoreSerializer.StoreToUri(new Element(), new Uri(excludedPath));

        var source = new Scene
        {
            Uri = new Uri(scenePath),
        };
        JsonObject json = CoreSerializer.SerializeToJsonObject(
            source,
            new CoreSerializerOptions { BaseUri = source.Uri });
        json["Elements"] = new JsonObject
        {
            ["Include"] = "**\\*.belm",
            ["Exclude"] = "elements\\excluded.belm",
        };
        json.JsonSave(scenePath);

        Scene restored = CoreSerializer.RestoreFromUri<Scene>(new Uri(scenePath));
        JsonObject roundTrip = CoreSerializer.SerializeToJsonObject(
            restored,
            new CoreSerializerOptions { BaseUri = restored.Uri });
        JsonObject elements = roundTrip["Elements"]!.AsObject();
        Assert.Multiple(() =>
        {
            Assert.That(restored.Children.Select(x => Path.GetFileName(x.Uri!.LocalPath)),
                Is.EquivalentTo(new[] { "included.belm" }));
            Assert.That((string?)elements["Include"], Is.EqualTo("**/*.belm"));
            Assert.That((string?)elements["Exclude"], Is.EqualTo("elements/excluded.belm"));
        });
    }
}
