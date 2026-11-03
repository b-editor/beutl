using System.Text.Json.Nodes;
using Beutl.Collections;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class SceneTests
{
    private string _tempDirectory = null!;

    // As the CoreProperty setters did, setting a list property replaces its items, so what is saved is what is shown.
    [Test]
    public void SettingAListPropertyReplacesItsItems()
    {
        var element = new Element();
        ICoreList<EngineObject> objects = element.Objects.Value;
        var shape = new RectShape();
        var scene = new Scene();
        ICoreList<TimelineLayer> layers = scene.Layers.Value;
        var layer = new TimelineLayer();

        element.Objects.Value = new CoreList<EngineObject> { shape };
        scene.Layers.Value = new CoreList<TimelineLayer> { layer };
        JsonObject json = CoreSerializer.SerializeToJsonObject(element);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(element.Objects.Value, Is.SameAs(objects));
            Assert.That(element.Objects, Is.EqualTo(new EngineObject[] { shape }));
            Assert.That(json["Objects"]!.AsArray().Select(item => Guid.Parse(item!["Id"]!.GetValue<string>())),
                Is.EqualTo(new[] { shape.Id.Value }));
            Assert.That(scene.Layers.Value, Is.SameAs(layers));
            Assert.That(scene.Layers, Is.EqualTo(new[] { layer }));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SceneReload_RejectsElementsOutsideTheSceneDirectory(bool symlink)
    {
        if (symlink && OperatingSystem.IsWindows()) Assert.Ignore("Requires symbolic link privileges.");
        string directory = Path.Combine(_tempDirectory, "scene");
        Directory.CreateDirectory(directory);
        string outside = Path.Combine(_tempDirectory, "private.belm");
        CoreSerializer.StoreToUri(new Element { Name = { Value = "outside" } }, new Uri(outside));
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
        scene.Children.Add(new Element { Uri = new Uri(elementPath), Name = { Value = "child" } });

        CoreSerializer.StoreToUri(scene, scene.Uri!);

        Assert.That(File.Exists(elementPath), Is.True);
    }

    // The glob reaches into every folder below the scene, where another scene can keep its own elements.
    [TestCase("inner.scene")]
    [TestCase("inner.SCENE")]
    public void A_scene_leaves_alone_the_elements_of_a_scene_in_a_folder_below_it(string innerFile)
    {
        var outer = new Scene { Uri = new Uri(Path.Combine(_tempDirectory, "outer.scene")) };
        var outerElement = new Element { Uri = new Uri(Path.Combine(_tempDirectory, "elements", "outer.belm")) };
        outer.Children.Add(outerElement);
        CoreSerializer.StoreToUri(outerElement, outerElement.Uri);
        CoreSerializer.StoreToUri(outer, outer.Uri);
        var inner = new Scene { Uri = new Uri(Path.Combine(_tempDirectory, "inner", innerFile)) };
        var innerElement = new Element { Uri = new Uri(Path.Combine(_tempDirectory, "inner", "elements", "inner.belm")) };
        inner.Children.Add(innerElement);
        CoreSerializer.StoreToUri(innerElement, innerElement.Uri);
        CoreSerializer.StoreToUri(inner, inner.Uri);

        Scene restoredOuter = CoreSerializer.RestoreFromUri<Scene>(outer.Uri);
        Scene restoredInner = CoreSerializer.RestoreFromUri<Scene>(inner.Uri);

        Assert.Multiple(() =>
        {
            Assert.That(restoredOuter.Children.Select(child => child.Id.Value), Is.EqualTo(new[] { outerElement.Id.Value }));
            Assert.That(restoredInner.Children.Select(child => child.Id.Value), Is.EqualTo(new[] { innerElement.Id.Value }));
        });
    }

    // The glob is also what brings back an element whose scene was not saved after it, as after a crash.
    [Test]
    public void A_scene_still_finds_an_element_it_did_not_list_in_a_folder_below_it()
    {
        var scene = new Scene { Uri = new Uri(Path.Combine(_tempDirectory, "main.scene")) };
        CoreSerializer.StoreToUri(scene, scene.Uri);
        var element = new Element { Uri = new Uri(Path.Combine(_tempDirectory, "some", "folder", "child.belm")) };
        CoreSerializer.StoreToUri(element, element.Uri);

        Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);

        Assert.That(restored.Children.Select(child => child.Id.Value), Is.EqualTo(new[] { element.Id.Value }));
    }

    // A save matches the children without looking for their files: the glob covers one not written yet, and a name it
    // does not cover gets a single include of its own.
    [TestCase("child.belm", new[] { "**/*.belm" })]
    [TestCase("child.clip", new[] { "**/*.belm", "elements/child.clip" })]
    public void Storing_scene_before_child_does_not_duplicate_include_patterns(string fileName, string[] expected)
    {
        var scene = new Scene { Uri = new Uri(Path.Combine(_tempDirectory, "project.scene")) };
        var element = new Element { Uri = new Uri(Path.Combine(_tempDirectory, "elements", fileName)) };
        scene.Children.Add(element);

        for (int i = 0; i < 3; i++)
        {
            CoreSerializer.StoreToUri(scene, scene.Uri, CoreSerializationMode.Write);
        }

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(element.Uri.LocalPath), Is.False);
            Assert.That(ReadPatterns(scene, "Include"), Is.EqualTo(expected));
        });

        CoreSerializer.StoreToUri(element, element.Uri);
        Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        Assert.That(restored.Children.Select(child => child.Id.Value), Is.EqualTo(new[] { element.Id.Value }));
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
            Assert.That(restored.Children.Select(child => child.Id.Value), Is.EqualTo(new[] { element.Id.Value }));
            Assert.That(patterns["Include"]!.GetValue<string>(), Is.EqualTo("**/*.belm"));
            Assert.That(patterns["Exclude"]!.GetValue<string>(), Is.EqualTo("elements/excluded.belm"));
        });
    }

    [Test]
    public void Storing_scene_drops_the_exclusion_of_a_deleted_file_and_keeps_globs()
    {
        var scene = new Scene { Uri = new Uri(Path.Combine(_tempDirectory, "project.scene")) };
        var element = new Element { Uri = new Uri(Path.Combine(_tempDirectory, "elements", "deleted.belm")) };
        scene.Children.Add(element);
        CoreSerializer.StoreToUri(scene, scene.Uri);
        JsonObject json = JsonNode.Parse(File.ReadAllText(scene.Uri.LocalPath))!.AsObject();
        json["Elements"]!["Exclude"] = "other/*.belm";
        json.JsonSave(scene.Uri.LocalPath);
        Scene loaded = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);

        loaded.RemoveChild(loaded.Children.Single());
        File.Delete(element.Uri.LocalPath);
        CoreSerializer.StoreToUri(loaded, scene.Uri, CoreSerializationMode.Write);

        JsonNode patterns = JsonNode.Parse(File.ReadAllText(scene.Uri.LocalPath))!["Elements"]!;
        Assert.That(patterns["Exclude"]!.GetValue<string>(), Is.EqualTo("other/*.belm"));
    }

    // The matcher ignores a directory exclusion's case, so a save keeps it even where the spelling differs on disk.
    [TestCase("ELEMENTS")]
    [TestCase("/ELEMENTS")]
    public void Storing_scene_keeps_a_directory_exclusion_in_another_case(string pattern)
    {
        (Scene scene, Element retained, _) = CreateSceneExcluding(pattern);

        Scene loaded = CoreSerializer.RestoreFromUri<Scene>(scene.Uri!);
        CoreSerializer.StoreToUri(loaded, scene.Uri!, CoreSerializationMode.Write);

        Scene reloaded = CoreSerializer.RestoreFromUri<Scene>(scene.Uri!);
        Assert.Multiple(() =>
        {
            Assert.That(ReadExclude(scene), Is.EqualTo(pattern));
            Assert.That(reloaded.Children.Select(child => child.Id.Value), Is.EqualTo(new[] { retained.Id.Value }));
        });
    }

    [Test]
    public void Storing_scene_keeps_the_exclusion_of_an_absent_directory()
    {
        (Scene scene, _, _) = CreateSceneExcluding("absent");

        Scene loaded = CoreSerializer.RestoreFromUri<Scene>(scene.Uri!);
        CoreSerializer.StoreToUri(loaded, scene.Uri!, CoreSerializationMode.Write);
        var returned = new Element { Uri = new Uri(Path.Combine(_tempDirectory, "absent", "returned.belm")) };
        CoreSerializer.StoreToUri(returned, returned.Uri);

        Scene reloaded = CoreSerializer.RestoreFromUri<Scene>(scene.Uri!);
        Assert.Multiple(() =>
        {
            Assert.That(ReadExclude(scene), Is.EqualTo("absent"));
            Assert.That(reloaded.Children.Select(child => child.Id.Value), Has.No.Member(returned.Id.Value));
        });
    }

    private (Scene Scene, Element Retained, Element Excluded) CreateSceneExcluding(string pattern)
    {
        var scene = new Scene { Uri = new Uri(Path.Combine(_tempDirectory, "project.scene")) };
        var retained = new Element { Uri = new Uri(Path.Combine(_tempDirectory, "retained.belm")) };
        var excluded = new Element { Uri = new Uri(Path.Combine(_tempDirectory, "elements", "excluded.belm")) };
        scene.Children.AddRange([retained, excluded]);
        CoreSerializer.StoreToUri(scene, scene.Uri);
        JsonObject json = JsonNode.Parse(File.ReadAllText(scene.Uri.LocalPath))!.AsObject();
        json["Elements"]!["Exclude"] = pattern;
        json.JsonSave(scene.Uri.LocalPath);
        return (scene, retained, excluded);
    }

    private static string? ReadExclude(Scene scene)
        => JsonNode.Parse(File.ReadAllText(scene.Uri!.LocalPath))!["Elements"]!["Exclude"]?.GetValue<string>();

    private static string[] ReadPatterns(Scene scene, string name)
        => JsonNode.Parse(File.ReadAllText(scene.Uri!.LocalPath))!["Elements"]![name] switch
        {
            JsonArray array => [.. array.Select(node => node!.GetValue<string>())],
            { } value => [value.GetValue<string>()],
            null => [],
        };

    // Exclusions are saved in the order their elements left; one that comes back and leaves again moves last.
    [Test]
    public void Storing_scene_keeps_exclusions_in_removal_order()
    {
        var scene = new Scene { Uri = new Uri(Path.Combine(_tempDirectory, "project.scene")) };
        Element[] elements = [.. Enumerable.Range(0, 3)
            .Select(i => new Element { Uri = new Uri(Path.Combine(_tempDirectory, "elements", $"{i}.belm")) })];
        scene.Children.AddRange(elements);
        CoreSerializer.StoreToUri(scene, scene.Uri);

        foreach (Element element in elements)
        {
            scene.Children.Remove(element);
        }

        scene.Children.Add(elements[1]);
        scene.Children.Remove(elements[1]);
        CoreSerializer.StoreToUri(scene, scene.Uri, CoreSerializationMode.Write);

        Assert.Multiple(() =>
        {
            Assert.That(ReadPatterns(scene, "Exclude"),
                Is.EqualTo(new[] { "elements/0.belm", "elements/2.belm", "elements/1.belm" }));
            Assert.That(CoreSerializer.RestoreFromUri<Scene>(scene.Uri).Children, Is.Empty);
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
            Assert.That(restored.Children.Select(child => child.Id.Value), Is.EqualTo(new[] { element.Id.Value }));
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
            Assert.That(restored.Children.Select(child => child.Id.Value), Is.EqualTo(new[] { retained.Id.Value }));
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

        Assert.That(restored.Children.Select(child => child.Id.Value), Is.EqualTo(new[] { retained.Id.Value }));
    }

    // A literal names a directory at that path from the scene folder, in the matcher's terms: any case, no leading
    // slash or dot segments, and an empty segment standing for any one directory. A path starting with ".." is outside.
    [TestCase("a", new[] { "top.belm", "x/a/b/five.belm" })]
    [TestCase("/A", new[] { "top.belm", "x/a/b/five.belm" })]
    [TestCase("a/b", new[] { "top.belm", "a/one.belm", "a/d/four.belm", "x/a/b/five.belm" })]
    [TestCase("A/./B", new[] { "top.belm", "a/one.belm", "a/d/four.belm", "x/a/b/five.belm" })]
    [TestCase("a/b/c", new[] { "top.belm", "a/one.belm", "a/b/two.belm", "a/d/four.belm", "x/a/b/five.belm" })]
    [TestCase("x/a", new[] { "top.belm", "a/one.belm", "a/b/two.belm", "a/b/c/three.belm", "a/d/four.belm" })]
    [TestCase("b", new[] { "top.belm", "a/one.belm", "a/b/two.belm", "a/b/c/three.belm", "a/d/four.belm", "x/a/b/five.belm" })]
    [TestCase("a//c", new[] { "top.belm", "a/one.belm", "a/b/two.belm", "a/d/four.belm", "x/a/b/five.belm" })]
    [TestCase("../a", new[] { "top.belm", "a/one.belm", "a/b/two.belm", "a/b/c/three.belm", "a/d/four.belm", "x/a/b/five.belm" })]
    public void SceneReload_ExcludesTheDirectoryALiteralPatternNames(string pattern, string[] expected)
    {
        var scene = new Scene { Uri = new Uri(Path.Combine(_tempDirectory, "project.scene")) };
        CoreSerializer.StoreToUri(scene, scene.Uri);
        foreach (string file in new[] { "top.belm", "a/one.belm", "a/b/two.belm", "a/b/c/three.belm", "a/d/four.belm", "x/a/b/five.belm" })
        {
            CoreSerializer.StoreToUri(new Element(), new Uri(Path.Combine(_tempDirectory, file)));
        }

        JsonObject json = JsonNode.Parse(File.ReadAllText(scene.Uri.LocalPath))!.AsObject();
        json["Elements"]!["Exclude"] = pattern;
        json.JsonSave(scene.Uri.LocalPath);

        Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);

        Assert.That(
            restored.Children.Select(child => Path.GetRelativePath(_tempDirectory, child.Uri!.LocalPath).Replace('\\', '/')),
            Is.EquivalentTo(expected));
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
