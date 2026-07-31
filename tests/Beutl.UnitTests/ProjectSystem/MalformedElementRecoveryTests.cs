using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.ProjectSystem;

public sealed class MalformedElementRecoveryTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "malformed-element-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Test]
    public void Save_PreservesMalformedElementSidecarBytes()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        byte[] corruptBytes = "{\"Id\":\"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0\",\"Objects\":["u8.ToArray();
        File.WriteAllBytes(elementPath, corruptBytes);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        recovered.Children.Single().Name = "Recovered placeholder";
        CoreSerializer.StoreToUri(recovered, sceneUri);

        Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(corruptBytes));
    }

    [Test]
    public void Restore_MalformedElementWithoutReadableId_UsesStableId()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        File.WriteAllText(elementPath, "{ this is not valid JSON");

        Guid first = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single().Id;
        Guid second = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single().Id;

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void Save_PreservesDeserializationFallbackSidecarBytes()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        json[nameof(CoreObject.Name)] = "Hand-formatted element";
        json[nameof(Element.Objects)]!.AsArray()[0]!.AsObject()[nameof(RectShape.Width)] = "invalid-width";
        string fallbackSource = json.ToJsonString();
        File.WriteAllText(elementPath, fallbackSource);
        byte[] originalBytes = File.ReadAllBytes(elementPath);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Assert.That(recovered.Children.Single().Objects.Single(), Is.InstanceOf<IFallback>());
        CoreSerializer.StoreToUri(recovered, sceneUri);

        Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
    }

    [Test]
    public void DirectElementSave_PreservesMalformedElementSidecarBytes()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        byte[] corruptBytes = "{\"Id\":\"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0\",\"Objects\":["u8.ToArray();
        File.WriteAllBytes(elementPath, corruptBytes);

        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
        recovered.Name = "Recovered placeholder";
        CoreSerializer.StoreToUri(recovered, recovered.Uri!);

        Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(corruptBytes));
    }

    [Test]
    public void AutoSave_PreservesMalformedElementSidecarBytes()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        byte[] corruptBytes = "{\"Id\":\"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0\",\"Objects\":["u8.ToArray();
        File.WriteAllBytes(elementPath, corruptBytes);

        Scene scene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        var app = new BeutlApplication { Project = new Project() };
        app.Project!.Items.Add(scene);
        Element recovered = scene.Children.Single();
        recovered.Name = "Recovered placeholder";
        using var service = new AutoSaveService();
        service.SaveObjects([recovered]);

        Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(corruptBytes));
    }

    [Test]
    public void AutoSave_RemovedMalformedElementPreservesSidecarBytes()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        byte[] corruptBytes = "{\"Id\":\"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0\",\"Objects\":["u8.ToArray();
        File.WriteAllBytes(elementPath, corruptBytes);

        Scene scene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        var app = new BeutlApplication { Project = new Project() };
        app.Project!.Items.Add(scene);
        Element recovered = scene.Children.Single();
        scene.Children.Remove(recovered);
        using var service = new AutoSaveService();
        service.SaveObjects([recovered]);

        Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(corruptBytes));
    }

    [Test]
    public void Restore_MalformedElementPrefersTopLevelId()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        var nestedId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var topLevelId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        File.WriteAllText(
            elementPath,
            $$"""{"Objects":[{"Id":"{{nestedId}}"}],"Id":"{{topLevelId}}","Broken":[""");

        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();

        Assert.That(recovered.Id, Is.EqualTo(topLevelId));
    }

    private (Uri SceneUri, string ElementPath) CreatePersistedScene()
    {
        var sceneUri = new Uri(Path.Combine(_root, "scene.scene"));
        string elementPath = Path.Combine(_root, "element.belm");
        var scene = new Scene(64, 64, "Scene")
        {
            Uri = sceneUri,
        };
        var element = new Element
        {
            Name = "Element",
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(elementPath),
        };
        element.AddObject(new RectShape
        {
            Width = { CurrentValue = 32 },
            Height = { CurrentValue = 32 },
        });
        scene.Children.Add(element);
        CoreSerializer.StoreToUri(scene, sceneUri);
        return (sceneUri, elementPath);
    }
}
