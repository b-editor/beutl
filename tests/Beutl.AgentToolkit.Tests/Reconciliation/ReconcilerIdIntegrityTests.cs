using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

public sealed class ReconcilerIdIntegrityTests
{
    [Test]
    public void Apply_repair_of_last_fallback_resumes_persistence_in_same_transaction()
    {
        Scene source = CreateSceneWithElement(out Element sourceElement);
        sourceElement.AddObject(new RectShape());
        CoreSerializer.StoreToUri(source, source.Uri!);
        string elementPath = sourceElement.Uri!.LocalPath;
        JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        elementJson[nameof(Element.Objects)]!.AsArray()[0]!.AsObject()["$type"]
            = "[Beutl.Engine]Beutl.Graphics.Shapes:MissingShape";
        File.WriteAllText(elementPath, elementJson.ToJsonString());
        byte[] originalBytes = File.ReadAllBytes(elementPath);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(source.Uri!);
        Element recoveredElement = recovered.Children.Single();
        var fallback = (EngineObject)recoveredElement.Objects.Single();
        using var session = new AgentToolkitTestSession(recovered);
        JsonObject desired = session.Documents.Read(recovered);
        JsonObject repairedJson = CoreSerializer.SerializeToJsonObject(new RectShape
        {
            Name = "Repaired shape",
        });
        // Omit the Id: the reconciler mints one for the inserted entity and treats the
        // subtree as new, the sanctioned replacement for a fallback whose type cannot
        // change in place.
        repairedJson.Remove(nameof(CoreObject.Id));
        JsonObject desiredElement = desired["Elements"]!.AsArray()[0]!.AsObject();
        desiredElement[nameof(Element.Objects)] = new JsonArray(repairedJson);

        var reconciler = new Reconciler();
        ReconcileResult result = reconciler.Apply(session, desired);
        CoreSerializer.StoreToUri(recovered, recovered.Uri!);
        byte[] repairedBytes = File.ReadAllBytes(elementPath);
        bool undone = session.History.Undo();
        File.WriteAllBytes(elementPath, originalBytes);
        CoreSerializer.StoreToUri(recovered, recovered.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That(result.Plan.Valid, Is.True);
            Assert.That(repairedBytes, Is.Not.EqualTo(originalBytes));
            Assert.That(undone, Is.True);
            Assert.That(recoveredElement.Objects.Single(), Is.SameAs(fallback));
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
        });
    }

    [Test]
    public void Mint_missing_ids_avoids_reserved_collisions()
    {
        JsonObject document = CreateDocumentWithIdlessRect(out string mintPath);
        var probe = (JsonObject)document.DeepClone();
        Guid defaultId = CollectionReconciler.CreateDeterministicId(
            mintPath,
            (JsonObject)probe["Elements"]![0]!["Objects"]![0]!);

        HashSet<Guid> unreservedMint = CollectionReconciler.MintMissingIds(probe);
        Assert.That(unreservedMint, Does.Contain(defaultId));

        var reserved = new HashSet<Guid> { defaultId };
        HashSet<Guid> minted = CollectionReconciler.MintMissingIds(document, reserved);

        Assert.Multiple(() =>
        {
            Assert.That(minted, Does.Not.Contain(defaultId));
            Assert.That(minted, Is.Not.Empty);
        });
    }

    [Test]
    public void Apply_edit_rejects_desired_document_with_duplicate_ids()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        var rect = new RectShape { Name = "mark", Width = { CurrentValue = 100 }, Height = { CurrentValue = 100 } };
        element.AddObject(rect);

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        JsonObject desired = session.Documents.Read(session.Root);
        var elementJson = (JsonObject)((JsonArray)desired["Elements"]!)
            .OfType<JsonObject>()
            .Single(item => item["Id"]!.GetValue<string>() == element.Id.ToString());
        var objects = (JsonArray)elementJson["Objects"]!;
        objects.Add(objects.OfType<JsonObject>().Single().DeepClone());

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error.Message, Does.Contain("more than once"));
        });
    }

    [Test]
    public void Apply_edit_rejects_desired_document_with_duplicate_ids_across_arrays()
    {
        Scene scene = CreateSceneWithElement(out Element firstElement);
        var secondElement = new Element
        {
            Start = TimeSpan.FromSeconds(4),
            Length = TimeSpan.FromSeconds(4),
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "second.belm"))
        };
        scene.Children.Add(secondElement);
        var rect = new RectShape { Name = "mark", Width = { CurrentValue = 100 }, Height = { CurrentValue = 100 } };
        firstElement.AddObject(rect);

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        JsonObject desired = session.Documents.Read(session.Root);
        var elements = (JsonArray)desired["Elements"]!;
        var secondElementJson = elements
            .OfType<JsonObject>()
            .Single(item => item["Id"]!.GetValue<string>() == secondElement.Id.ToString());
        secondElementJson["Objects"] = new JsonArray(new JsonObject
        {
            ["$type"] = IdentityHelper.WriteDiscriminator(typeof(RectShape)),
            ["Id"] = rect.Id.ToString(),
            ["Name"] = "duplicate mark"
        });

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error.Message, Does.Contain("more than once"));
        });
    }

    [Test]
    public void Apply_edit_rejects_an_element_reusing_the_scene_root_id()
    {
        Scene scene = CreateSceneWithElement(out Element element);

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        JsonObject desired = session.Documents.Read(session.Root);
        var elementJson = (JsonObject)((JsonArray)desired["Elements"]!)
            .OfType<JsonObject>()
            .Single(item => item["Id"]!.GetValue<string>() == element.Id.ToString());
        elementJson["Id"] = scene.Id.ToString();

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error.Message, Does.Contain("more than once"));
        });
    }

    [Test]
    public void Apply_edit_still_works_on_document_with_preexisting_duplicate_ids()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        var rotationA = new RotationTransform { Rotation = { CurrentValue = -18 } };
        var rotationB = new RotationTransform { Rotation = { CurrentValue = -18 } };
        rotationB.Id = rotationA.Id;
        var rect = new RectShape
        {
            Name = "pane",
            Width = { CurrentValue = 100 },
            Height = { CurrentValue = 100 },
            Transform =
            {
                CurrentValue = new TransformGroup
                {
                    Children = { rotationA, new TranslateTransform(), rotationB }
                }
            }
        };
        element.AddObject(rect);

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        JsonObject renamePatch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                ["Id"] = element.Id.ToString(),
                ["Name"] = "[role:decorative] pane (converge sweep)"
            })
        };

        ToolResult<ApplyEditResponse> renamed = tools.ApplyEdit(patch: renamePatch, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(renamed.IsSuccess, Is.True, renamed.Error?.Message);
            Assert.That(element.Name, Is.EqualTo("[role:decorative] pane (converge sweep)"));
        });
    }

    [Test]
    public void Apply_edit_allows_preexisting_fallback_in_nonhierarchical_property_value()
    {
        Scene scene = CreateSceneWithElement(out Element healthy);
        string directory = Path.GetDirectoryName(scene.Uri!.LocalPath)!;
        var carrier = new Element
        {
            Start = TimeSpan.FromSeconds(4),
            Length = TimeSpan.FromSeconds(4),
            Uri = new Uri(Path.Combine(directory, "carrier.belm")),
        };
        var holder = new NonHierarchicalValueHolder();
        holder.Value.CurrentValue = new RectShape();
        carrier.AddObject(holder);
        scene.Children.Add(carrier);
        CoreSerializer.StoreToUri(scene, scene.Uri!);

        JsonObject carrierJson = JsonNode.Parse(File.ReadAllText(carrier.Uri.LocalPath))!.AsObject();
        JsonObject valueJson = carrierJson[nameof(Element.Objects)]!.AsArray()[0]!
            [nameof(NonHierarchicalValueHolder.Value)]!.AsObject();
        valueJson["$type"] = "[Beutl.Engine]Beutl.Engine:MissingPropertyValue";
        valueJson.Remove(nameof(CoreObject.Id));
        File.WriteAllText(carrier.Uri.LocalPath, carrierJson.ToJsonString());

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(scene.Uri!);
        Element recoveredHealthy = recovered.Children.Single(item => item.Id == healthy.Id);

        using var session = new AgentToolkitTestSession(recovered);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);
        JsonObject renamePatch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                ["Id"] = healthy.Id.ToString(),
                ["Name"] = "Renamed healthy element",
            }),
        };

        ToolResult<ApplyEditResponse> renamed = tools.ApplyEdit(
            patch: renamePatch,
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(renamed.IsSuccess, Is.True, renamed.Error?.Message);
            Assert.That(recoveredHealthy.Name, Is.EqualTo("Renamed healthy element"));
        });
    }

    private static JsonObject CreateDocumentWithIdlessRect(out string mintPath)
    {
        mintPath = "$/Elements[0]/Objects[0]";
        return new JsonObject
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                ["Id"] = Guid.NewGuid().ToString(),
                ["Objects"] = new JsonArray(new JsonObject
                {
                    ["$type"] = IdentityHelper.WriteDiscriminator(typeof(RectShape)),
                    ["Name"] = "pane rotation",
                    ["Width"] = 100
                })
            })
        };
    }

    private static Scene CreateSceneWithElement(out Element element)
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var scene = new Scene(1920, 1080, "Scene")
        {
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
        element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(4),
            Uri = new Uri(Path.Combine(dir, "element.belm"))
        };
        scene.Children.Add(element);
        return scene;
    }

    public sealed class NonHierarchicalValueHolder : EngineObject
    {
        public NonHierarchicalValueHolder()
        {
            Value.SetAttributes(nameof(Value), []);
            Value.SetValidator(Value.CreateValidator([]));
            RegisterProperty(Value);
        }

        public IProperty<EngineObject?> Value { get; } = Property.Create<EngineObject?>();
    }
}
