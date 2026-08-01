using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Graphics;
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
    public void Restore_MalformedElementsWithoutReadableIds_UseStableDistinctIds()
    {
        (Uri sceneUri, string[] elementPaths) = CreatePersistedSceneWithElements(
            "element-a.belm",
            "element-b.belm");
        foreach (string elementPath in elementPaths)
            File.WriteAllText(elementPath, "{ this is not valid JSON");

        Dictionary<string, Guid> first = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children
            .ToDictionary(element => element.Uri!.LocalPath, element => element.Id);
        Dictionary<string, Guid> second = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children
            .ToDictionary(element => element.Uri!.LocalPath, element => element.Id);

        Assert.Multiple(() =>
        {
            Assert.That(first[elementPaths[0]], Is.Not.EqualTo(Guid.Empty));
            Assert.That(first[elementPaths[1]], Is.Not.EqualTo(Guid.Empty));
            Assert.That(second[elementPaths[0]], Is.EqualTo(first[elementPaths[0]]));
            Assert.That(second[elementPaths[1]], Is.EqualTo(first[elementPaths[1]]));
            Assert.That(first[elementPaths[0]], Is.Not.EqualTo(first[elementPaths[1]]));
        });
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
    public void Restore_DeserializationFallbackPreservesOriginalDiscriminator()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        JsonObject malformedObject = json[nameof(Element.Objects)]!.AsArray()[0]!.AsObject();
        string originalType = malformedObject["$type"]!.GetValue<string>();
        malformedObject[nameof(RectShape.Width)] = "invalid-width";
        File.WriteAllText(elementPath, json.ToJsonString());

        IFallback fallback = (IFallback)CoreSerializer
            .RestoreFromUri<Scene>(sceneUri)
            .Children.Single()
            .Objects.Single();

        Assert.Multiple(() =>
        {
            Assert.That(fallback.TryGetTypeName(out string? typeName), Is.True);
            Assert.That(typeName, Is.EqualTo(originalType));
            Assert.That(fallback.Json!["$type"]!.GetValue<string>(), Is.EqualTo(originalType));
        });
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
        var errors = new List<Exception>();
        using var subscription = service.SaveError.Subscribe(errors.Add);
        service.SaveObjects([recovered]);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, "Autosave must complete without swallowing a serialization error.");
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(corruptBytes));
        });
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
        var errors = new List<Exception>();
        using var subscription = service.SaveError.Subscribe(errors.Add);
        service.SaveObjects([recovered]);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, "Autosave must complete without swallowing a serialization error.");
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(corruptBytes));
        });
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

    [Test]
    public void Restore_NonElementTopLevelDiscriminatorCreatesDisabledRecoveryElement()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        json["$type"] = CoreSerializer.SerializeToJsonObject(new RectShape())["$type"]!.DeepClone();
        File.WriteAllText(elementPath, json.ToJsonString());
        byte[] originalBytes = File.ReadAllBytes(elementPath);

        Scene scene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element recovered = scene.Children.Single();
        var fallback = (IFallback)recovered.Objects.Single();

        Assert.Multiple(() =>
        {
            Assert.That(recovered.IsEnabled, Is.False);
            Assert.That(recovered.IsStorageWriteSuppressed, Is.True);
            Assert.That(fallback.Reason, Is.EqualTo(FallbackReason.DeserializationFailed));
            Assert.That(fallback.ErrorMessage, Does.Contain("JsonException"));
            Assert.That(fallback.ErrorMessage, Does.Contain("is not assignable"));
        });

        CoreSerializer.StoreToUri(scene, sceneUri);
        Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
    }

    [Test]
    public void DeserializeFromJsonObject_IncompatibleDiscriminatorFallsBackBeforeConstruction()
    {
        JsonObject json = CreateIncompatibleDrawableJson();

        object result = CoreSerializer.DeserializeFromJsonObject(json, typeof(Drawable));

        AssertIncompatibleDrawableFallback(result);
    }

    [Test]
    public void DeserializeFromJsonNode_IncompatibleDiscriminatorFallsBackBeforeConstruction()
    {
        JsonObject json = CreateIncompatibleDrawableJson();

        object? result = CoreSerializer.DeserializeFromJsonNode(json, typeof(Drawable));

        AssertIncompatibleDrawableFallback(result);
    }

    private static JsonObject CreateIncompatibleDrawableJson()
    {
        IncompatibleSerializable.ConstructorCount = 0;
        var json = new JsonObject();
        json.WriteDiscriminator(typeof(IncompatibleSerializable));
        return json;
    }

    private static void AssertIncompatibleDrawableFallback(object? result)
    {
        var fallback = (IFallback)result!;
        Assert.Multiple(() =>
        {
            Assert.That(IncompatibleSerializable.ConstructorCount, Is.Zero,
                "An incompatible discriminator must be rejected before construction.");
            Assert.That(fallback.Reason, Is.EqualTo(FallbackReason.DeserializationFailed));
            Assert.That(fallback.ErrorMessage, Does.Contain("is not assignable"));
        });
    }

    private (Uri SceneUri, string ElementPath) CreatePersistedScene()
    {
        (Uri sceneUri, string[] elementPaths) = CreatePersistedSceneWithElements("element.belm");
        return (sceneUri, elementPaths.Single());
    }

    private (Uri SceneUri, string[] ElementPaths) CreatePersistedSceneWithElements(
        params string[] elementFileNames)
    {
        var sceneUri = new Uri(Path.Combine(_root, "scene.scene"));
        var scene = new Scene(64, 64, "Scene")
        {
            Uri = sceneUri,
        };
        string[] elementPaths = elementFileNames
            .Select(elementFileName => Path.Combine(_root, elementFileName))
            .ToArray();
        foreach (string elementPath in elementPaths)
        {
            var element = new Element
            {
                Name = Path.GetFileNameWithoutExtension(elementPath),
                Length = TimeSpan.FromSeconds(1),
                Uri = new Uri(elementPath),
            };
            element.AddObject(new RectShape
            {
                Width = { CurrentValue = 32 },
                Height = { CurrentValue = 32 },
            });
            scene.Children.Add(element);
        }

        CoreSerializer.StoreToUri(scene, sceneUri);
        return (sceneUri, elementPaths);
    }

    public sealed class IncompatibleSerializable : ICoreSerializable
    {
        public IncompatibleSerializable()
        {
            ConstructorCount++;
        }

        public static int ConstructorCount { get; set; }

        public void Serialize(ICoreSerializationContext context)
        {
        }

        public void Deserialize(ICoreSerializationContext context)
        {
        }
    }
}
