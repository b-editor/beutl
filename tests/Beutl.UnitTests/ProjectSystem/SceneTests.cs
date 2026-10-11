using System.Text.Json.Nodes;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class SceneTests
{
    private string _tempDirectory = null!;

    [TestCase(false)]
    [TestCase(true)]
    public void SceneReload_RejectsElementsOutsideTheSceneDirectory(bool symlink)
    {
        if (symlink && OperatingSystem.IsWindows()) Assert.Ignore("Requires symbolic link privileges.");
        string directory = Path.Combine(_tempDirectory, "scene");
        Directory.CreateDirectory(directory);
        string outside = Path.Combine(_tempDirectory, "private.belm");
        CoreSerializer.StoreToUri(new Element { Name = "outside" }, new Uri(outside));
        byte[] original = File.ReadAllBytes(outside);
        var scene = new Scene { Uri = new Uri(Path.Combine(directory, "main.scene")) };
        JsonObject json = CoreSerializer.SerializeToJsonObject(scene, new CoreSerializerOptions { BaseUri = scene.Uri });
        if (symlink) File.CreateSymbolicLink(Path.Combine(directory, "linked.belm"), outside);
        json["Elements"] = new JsonObject { ["Include"] = symlink ? "linked.belm" : "../private.belm" };
        json.JsonSave(scene.Uri.LocalPath);
        Assert.Throws<System.Text.Json.JsonException>(() => CoreSerializer.RestoreFromUri<Scene>(scene.Uri));
        Assert.That(File.ReadAllBytes(outside), Is.EqualTo(original));
    }

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
    public void Storing_scene_before_child_does_not_duplicate_include_patterns()
    {
        var scene = new Scene { Uri = new Uri(Path.Combine(_tempDirectory, "project.scene")) };
        var element = new Element { Uri = new Uri(Path.Combine(_tempDirectory, "elements", "child.belm")) };
        scene.Children.Add(element);

        for (int i = 0; i < 3; i++)
        {
            CoreSerializer.StoreToUri(scene, scene.Uri, CoreSerializationMode.Write);
        }

        JsonNode patterns = JsonNode.Parse(File.ReadAllText(scene.Uri.LocalPath))!["Elements"]!;
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(element.Uri.LocalPath), Is.False);
            Assert.That(patterns["Include"]!.AsArray().Select(node => node!.GetValue<string>()),
                Is.EqualTo(new[] { "**/*.belm", "elements/child.belm" }));
        });

        CoreSerializer.StoreToUri(element, element.Uri);
        Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        Assert.That(restored.Children.Select(child => child.Id), Is.EqualTo(new[] { element.Id }));
    }

    [Test]
    public void Storing_scene_clears_stale_exclusion_for_replaced_child_only()
    {
        var scene = new Scene { Uri = new Uri(Path.Combine(_tempDirectory, "project.scene")) };
        var element = new Element { Uri = new Uri(Path.Combine(_tempDirectory, "elements", "child.belm")) };
        var excluded = new Element { Uri = new Uri(Path.Combine(_tempDirectory, "elements", "excluded.belm")) };
        scene.Children.AddRange([element, excluded]);
        CoreSerializer.StoreToUri(scene, scene.Uri);

        scene.Children.Remove(element);
        scene.Children.Remove(excluded);
        File.Delete(element.Uri.LocalPath);
        scene.Children.Add(new Element());
        // Replacement raises Replace instead of Add, leaving the old exclusion until serialization.
        scene.Children[0] = element;

        CoreSerializer.StoreToUri(scene, scene.Uri);

        Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        JsonNode patterns = JsonNode.Parse(File.ReadAllText(scene.Uri.LocalPath))!["Elements"]!;
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(excluded.Uri.LocalPath), Is.True);
            Assert.That(restored.Children.Select(child => child.Id), Is.EqualTo(new[] { element.Id }));
            Assert.That(patterns["Include"]!.GetValue<string>(), Is.EqualTo("**/*.belm"));
            Assert.That(patterns["Exclude"]!.GetValue<string>(), Is.EqualTo("elements/excluded.belm"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SceneReload_PreservesRestoredChildWithDifferentPathCasing(bool replace)
    {
        var scene = new Scene { Uri = new Uri(Path.Combine(_tempDirectory, "project.scene")) };
        var element = new Element { Uri = new Uri(Path.Combine(_tempDirectory, "elements", "child.belm")) };
        var restoredUri = new Uri(Path.Combine(_tempDirectory, "elements", "CHILD.belm"));
        scene.Children.Add(element);
        CoreSerializer.StoreToUri(scene, scene.Uri);
        if (!File.Exists(restoredUri.LocalPath))
            Assert.Ignore("This volume distinguishes case-sensitive element names.");

        scene.Children.Remove(element);
        File.Delete(element.Uri.LocalPath);
        element.Uri = restoredUri;
        if (replace)
        {
            scene.Children.Add(new Element());
            scene.Children[0] = element;
        }
        else
        {
            scene.Children.Add(element);
        }

        CoreSerializer.StoreToUri(scene, scene.Uri);

        Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        JsonNode patterns = JsonNode.Parse(File.ReadAllText(scene.Uri.LocalPath))!["Elements"]!;
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(element.Uri.LocalPath), Is.True);
            Assert.That(restored.Children.Select(child => child.Id), Is.EqualTo(new[] { element.Id }));
            Assert.That(patterns["Exclude"], Is.Null);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SceneReload_KeepsCaseDistinctElementExcluded(bool addAfterExclusion)
    {
        var scene = new Scene { Uri = new Uri(Path.Combine(_tempDirectory, "project.scene")) };
        var excluded = new Element { Uri = new Uri(Path.Combine(_tempDirectory, "elements", "child.belm")) };
        var retained = new Element { Uri = new Uri(Path.Combine(_tempDirectory, "elements", "CHILD.belm")) };
        scene.Children.Add(excluded);
        CoreSerializer.StoreToUri(scene, scene.Uri);
        if (File.Exists(retained.Uri.LocalPath))
            Assert.Ignore("This volume does not distinguish case-sensitive element names.");

        if (!addAfterExclusion)
            scene.Children.Add(retained);
        scene.Children.Remove(excluded);
        if (addAfterExclusion)
            scene.Children.Add(retained);
        CoreSerializer.StoreToUri(scene, scene.Uri);

        Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        JsonNode patterns = JsonNode.Parse(File.ReadAllText(scene.Uri.LocalPath))!["Elements"]!;
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(excluded.Uri.LocalPath), Is.True);
            Assert.That(restored.Children.Select(child => child.Id), Is.EqualTo(new[] { retained.Id }));
            Assert.That(patterns["Exclude"]!.GetValue<string>(), Is.EqualTo("elements/child.belm"));
        });
    }

    [TestCase("elements/*.belm")]
    [TestCase("elements/")]
    [TestCase("elements")]
    [TestCase("/elements")]
    [TestCase("ELEMENTS")]
    [TestCase("/ELEMENTS")]
    [TestCase("./elements/excluded.belm")]
    [TestCase("/elements/excluded.belm")]
    public void SceneReload_PreservesExclusionPatterns(string pattern)
    {
        var scene = new Scene { Uri = new Uri(Path.Combine(_tempDirectory, "project.scene")) };
        var retained = new Element { Uri = new Uri(Path.Combine(_tempDirectory, "retained.belm")) };
        var excluded = new Element { Uri = new Uri(Path.Combine(_tempDirectory, "elements", "excluded.belm")) };
        scene.Children.AddRange([retained, excluded]);
        CoreSerializer.StoreToUri(scene, scene.Uri);
        JsonObject json = JsonNode.Parse(File.ReadAllText(scene.Uri.LocalPath))!.AsObject();
        json["Elements"]!["Exclude"] = pattern;
        json.JsonSave(scene.Uri.LocalPath);

        Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);

        Assert.That(restored.Children.Select(child => child.Id), Is.EqualTo(new[] { retained.Id }));
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
