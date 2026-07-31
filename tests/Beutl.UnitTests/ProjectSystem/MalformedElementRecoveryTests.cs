using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
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

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.EqualTo(Guid.Empty));
            Assert.That(second, Is.EqualTo(first));
        });
    }

    [Test]
    public void Restore_MalformedElementWithOnlyNestedId_DoesNotAdoptIt()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        var nestedId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        File.WriteAllText(
            elementPath,
            $$"""{"Objects":[{"Id":"{{nestedId}}"}],"Broken":[""");

        Guid recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single().Id;

        Assert.Multiple(() =>
        {
            Assert.That(recovered, Is.Not.EqualTo(nestedId));
            Assert.That(recovered, Is.Not.EqualTo(Guid.Empty));
        });
    }

    [Test]
    public void Restore_ResolvableNonElementDiscriminator_RecoversInsteadOfFailing()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        File.WriteAllText(
            elementPath,
            """{"$type":"[Beutl.Engine]Beutl.Graphics.Shapes:RectShape","Id":"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0"}""");
        byte[] originalBytes = File.ReadAllBytes(elementPath);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        CoreSerializer.StoreToUri(recovered, sceneUri);

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Children.Single().IsEnabled, Is.False);
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
        });
    }

    [Test]
    public void Restore_UnparsableDiscriminator_RecoversInsteadOfFailing()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        File.WriteAllText(
            elementPath,
            """{"$type":"x","Id":"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0"}""");
        byte[] originalBytes = File.ReadAllBytes(elementPath);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        CoreSerializer.StoreToUri(recovered, sceneUri);

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Children.Single().IsEnabled, Is.False);
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
        });
    }

    [Test]
    public void Restore_UnresolvableGenericDiscriminator_RecoversInsteadOfFailing()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        File.WriteAllText(
            elementPath,
            """{"$type":"[NoSuchAssembly]Ns:Foo<[System.Private.CoreLib]System:Int32>","Id":"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0"}""");
        byte[] originalBytes = File.ReadAllBytes(elementPath);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        CoreSerializer.StoreToUri(recovered, sceneUri);

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Children.Single().IsEnabled, Is.False);
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
        });
    }

    [Test]
    public void Restore_SceneDiscriminatorWithSelfInclude_RecoversWithoutRecursing()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        File.WriteAllText(
            elementPath,
            """{"$type":"[Beutl.ProjectSystem]:Scene","Id":"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0","Elements":{"Include":["element.belm"]}}""");
        byte[] originalBytes = File.ReadAllBytes(elementPath);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        CoreSerializer.StoreToUri(recovered, sceneUri);

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Children.Single().IsEnabled, Is.False);
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
        });
    }

    [Test]
    public void Restore_NestedSceneDiscriminatorWithSelfInclude_RecoversWithoutRecursing()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        elementJson[nameof(Element.Objects)] = new JsonArray(new JsonObject
        {
            ["$type"] = "[Beutl.ProjectSystem]:Scene",
            [nameof(CoreObject.Uri)] = "element.belm",
            ["Elements"] = new JsonObject
            {
                ["Include"] = new JsonArray("element.belm"),
            },
        });
        File.WriteAllText(elementPath, elementJson.ToJsonString());
        byte[] originalBytes = File.ReadAllBytes(elementPath);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        CoreSerializer.StoreToUri(recovered, sceneUri);

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Children.Single().Objects.Single(), Is.InstanceOf<IFallback>());
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
        });
    }

    [Test]
    public void DeserializeFromJsonObject_UnassignableDiscriminatorUsesFallback()
    {
        var json = new JsonObject
        {
            ["$type"] = "[Beutl.ProjectSystem]:Scene",
        };

        object restored = CoreSerializer.DeserializeFromJsonObject(json, typeof(EngineObject));

        Assert.That(restored, Is.InstanceOf<IFallback>());
    }

    [Test]
    public void SaveAs_PreservesBomAndNonUtf8SidecarBytes()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        byte[] corruptBytes =
        [
            0xEF, 0xBB, 0xBF,
            .. "{\"Id\":\"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0\",\"Objects\":["u8,
            0xFF, 0xFE, 0x00,
        ];
        File.WriteAllBytes(elementPath, corruptBytes);

        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
        string rehomedPath = Path.Combine(_root, "rehomed", Path.GetFileName(elementPath));
        CoreSerializer.StoreToUri(recovered, new Uri(rehomedPath));

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Id, Is.EqualTo(new Guid("85f4d478-e16d-4cb1-ab71-ee1a90a03fe0")));
            Assert.That(File.ReadAllBytes(rehomedPath), Is.EqualTo(corruptBytes));
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(corruptBytes));
        });
    }

    [Test]
    public void StoreToUri_AfterRehome_KeepsTheOriginalSkipProtected()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        byte[] corruptBytes = "{\"Id\":\"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0\",\"Objects\":["u8.ToArray();
        File.WriteAllBytes(elementPath, corruptBytes);

        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
        string rehomedPath = Path.Combine(_root, "rehomed", Path.GetFileName(elementPath));
        CoreSerializer.StoreToUri(recovered, new Uri(rehomedPath));

        // A failed multi-file save-as rolls Uri back to the original; the next ordinary save must
        // still treat the original sidecar as the protected source, not a rehome target.
        File.Delete(elementPath);
        CoreSerializer.StoreToUri(recovered, new Uri(elementPath));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(elementPath), Is.False);
            Assert.That(File.ReadAllBytes(rehomedPath), Is.EqualTo(corruptBytes));
        });
    }

    [Test]
    public void Restore_NonStringDiscriminator_RecoversInsteadOfLoadingLegacyDefault()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        File.WriteAllText(
            elementPath,
            """{"$type":123,"Id":"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0"}""");
        byte[] originalBytes = File.ReadAllBytes(elementPath);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        CoreSerializer.StoreToUri(recovered, sceneUri);

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Children.Single().IsEnabled, Is.False);
            Assert.That(
                recovered.Children.Single().Id,
                Is.EqualTo(new Guid("85f4d478-e16d-4cb1-ab71-ee1a90a03fe0")));
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
        });
    }

    [Test]
    public void Restore_InvalidElementScalarValue_RecoversInsteadOfFailing()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        json[nameof(Element.AccentColor)] = "not-a-color";
        File.WriteAllText(elementPath, json.ToJsonString());
        byte[] originalBytes = File.ReadAllBytes(elementPath);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        CoreSerializer.StoreToUri(recovered, sceneUri);

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Children.Single().IsEnabled, Is.False);
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
        });
    }

    [Test]
    public void Save_PreservesSidecarBytesWhenFallbackIsOutsideTheHierarchy()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        Scene loaded = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element loadedElement = loaded.Children.Single();
        var shape = (RectShape)loadedElement.Objects.Single();
        shape.Transform.CurrentValue = new RotationTransform();
        CoreSerializer.StoreToUri(loadedElement, loadedElement.Uri!);

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        Assert.That(
            ReplaceDiscriminator(json, "Transformation", "[Beutl.Engine]Beutl.Graphics.Transformation:DoesNotExist"),
            Is.True);
        File.WriteAllText(elementPath, json.ToJsonString());
        byte[] originalBytes = File.ReadAllBytes(elementPath);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        CoreSerializer.StoreToUri(recovered, sceneUri);

        Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
    }

    [Test]
    public void Restore_DuplicateTopLevelId_YieldsToTheHealthyElement()
    {
        var sceneUri = new Uri(Path.Combine(_root, "scene.scene"));
        string element1Path = Path.Combine(_root, "element1.belm");
        string element2Path = Path.Combine(_root, "element2.belm");
        var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
        scene.Children.Add(new Element
        {
            Name = "One",
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(element1Path),
        });
        scene.Children.Add(new Element
        {
            Name = "Two",
            Start = TimeSpan.FromSeconds(1),
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(element2Path),
        });
        Guid healthyId = scene.Children[0].Id;
        CoreSerializer.StoreToUri(scene, sceneUri);
        File.WriteAllText(element2Path, $$"""{"Id":"{{healthyId}}","Objects":[""");

        Scene firstLoad = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Scene secondLoad = CoreSerializer.RestoreFromUri<Scene>(sceneUri);

        Element healthy = firstLoad.Children.Single(c => c.Name == "One");
        Element corrupt = firstLoad.Children.Single(c => c.Name != "One");
        Element corruptAgain = secondLoad.Children.Single(c => c.Name != "One");
        Assert.Multiple(() =>
        {
            Assert.That(healthy.Id, Is.EqualTo(healthyId));
            Assert.That(corrupt.Id, Is.Not.EqualTo(healthyId));
            Assert.That(corrupt.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(corruptAgain.Id, Is.EqualTo(corrupt.Id));
        });
    }

    [Test]
    public void StoreToUri_RehomeTarget_NeverOverwritesAnExistingFile()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        byte[] corruptBytes = "{\"Id\":\"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0\",\"Objects\":["u8.ToArray();
        File.WriteAllBytes(elementPath, corruptBytes);

        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
        string rehomedPath = Path.Combine(_root, "rehomed", Path.GetFileName(elementPath));
        CoreSerializer.StoreToUri(recovered, new Uri(rehomedPath));

        byte[] repairedBytes = "{\"Id\":\"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0\",\"Objects\":[]}"u8.ToArray();
        File.WriteAllBytes(rehomedPath, repairedBytes);
        CoreSerializer.StoreToUri(recovered, new Uri(rehomedPath));

        Assert.That(File.ReadAllBytes(rehomedPath), Is.EqualTo(repairedBytes));
    }

    [Test]
    public void SaveAs_CopiesRecoveredSidecarBytesToTheNewLocation()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        byte[] corruptBytes = "{\"Id\":\"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0\",\"Objects\":["u8.ToArray();
        File.WriteAllBytes(elementPath, corruptBytes);

        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
        string rehomedPath = Path.Combine(_root, "rehomed", Path.GetFileName(elementPath));
        CoreSerializer.StoreToUri(recovered, new Uri(rehomedPath));

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(rehomedPath), Is.EqualTo(corruptBytes));
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(corruptBytes));
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
    public void DeleteChild_PreservesRecoveredSidecarAndDeletesNormalSidecar()
    {
        (Uri sceneUri, string recoveredPath) = CreatePersistedScene();
        byte[] corruptBytes = "{\"Id\":\"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0\",\"Objects\":["u8.ToArray();
        File.WriteAllBytes(recoveredPath, corruptBytes);

        Scene scene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element recovered = scene.Children.Single();
        string normalPath = Path.Combine(_root, "normal.belm");
        var normal = new Element
        {
            Name = "Normal",
            Start = TimeSpan.FromSeconds(1),
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(normalPath),
        };
        normal.AddObject(new RectShape());
        CoreSerializer.StoreToUri(normal, normal.Uri!);
        scene.Children.Add(normal);

        scene.DeleteChild(recovered);
        scene.DeleteChild(normal);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(recoveredPath), Is.EqualTo(corruptBytes));
            Assert.That(File.Exists(normalPath), Is.False);
            Assert.That(scene.Children, Is.Empty);
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

    private static bool ReplaceDiscriminator(JsonNode node, string containsToken, string replacement)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("$type", out JsonNode? typeNode)
                && typeNode is JsonValue typeValue
                && typeValue.TryGetValue(out string? typeName)
                && typeName.Contains(containsToken))
            {
                obj["$type"] = replacement;
                return true;
            }

            foreach ((string _, JsonNode? child) in obj)
            {
                if (child != null && ReplaceDiscriminator(child, containsToken, replacement))
                {
                    return true;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array)
            {
                if (child != null && ReplaceDiscriminator(child, containsToken, replacement))
                {
                    return true;
                }
            }
        }

        return false;
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
