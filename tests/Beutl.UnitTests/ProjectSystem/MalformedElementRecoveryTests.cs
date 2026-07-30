using System.Text.Json.Nodes;
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
