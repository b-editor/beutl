using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

public sealed class ReconcilerIdIntegrityTests
{
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
}
