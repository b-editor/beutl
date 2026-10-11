using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

// Clip and layer locks are an editor rule (Scene.IsElementLocked). Agent edits follow it: a locked
// element cannot change, nothing lands on a locked layer, and only the user unlocks.
public sealed class ApplyEditLockTests
{
    private const int LockedLayer = 2;

    [TestCase("start")]
    [TestCase("delete")]
    [TestCase("insert")]
    public void Apply_edit_rejects_edits_under_a_locked_element_or_layer(string edit)
    {
        Scene scene = CreateScene();
        Element locked = AddElement(scene, "Locked title", zIndex: 0);
        locked.IsLocked = true;
        scene.Layers.Add(new TimelineLayer { ZIndex = LockedLayer, IsLocked = true });
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);
        string before = session.Documents.Read(scene).ToJsonString();

        JsonObject patch = edit switch
        {
            "start" => ElementPatch(locked, new JsonObject { [nameof(Element.Start)] = TimeSpan.FromSeconds(5).ToString("c") }),
            "delete" => ElementPatch(locked, new JsonObject { ["$delete"] = true }),
            _ => InsertPatch("New title", LockedLayer),
        };

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error.Message, edit == "insert"
                ? Does.Contain($"Layer {LockedLayer} is locked").And.Contain("'New title'")
                : Does.Contain($"'Locked title' ({locked.Id}) is locked"));
            Assert.That(result.Error.Hint, Does.Contain("ask the user to unlock"));
            Assert.That(session.Documents.Read(scene).ToJsonString(), Is.EqualTo(before));
        });
    }

    [Test]
    public void Apply_edit_rejects_edits_to_an_element_on_a_locked_layer()
    {
        Scene scene = CreateScene();
        Element element = AddElement(scene, "Title", LockedLayer);
        scene.Layers.Add(new TimelineLayer { ZIndex = LockedLayer, IsLocked = true });
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: ElementPatch(element, new JsonObject { [nameof(Element.ZIndex)] = 0 }),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Message, Does.Contain($"is on locked layer {LockedLayer}"));
            Assert.That(result.Error.Target, Is.EqualTo($"$/Elements[Id={element.Id}]/ZIndex"));
            Assert.That(element.ZIndex, Is.EqualTo(LockedLayer));
        });
    }

    [Test]
    public void Apply_edit_rejects_moving_an_element_onto_a_locked_layer()
    {
        Scene scene = CreateScene();
        Element element = AddElement(scene, "Title", zIndex: 0);
        scene.Layers.Add(new TimelineLayer { ZIndex = LockedLayer, IsLocked = true });
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: ElementPatch(element, new JsonObject { [nameof(Element.ZIndex)] = LockedLayer }),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Message, Does.Contain($"Layer {LockedLayer} is locked"));
            Assert.That(element.ZIndex, Is.Zero);
        });
    }

    [Test]
    public void Apply_edit_lets_an_agent_lock_elements_and_layers()
    {
        Scene scene = CreateScene();
        Element element = AddElement(scene, "Title", zIndex: 0);
        Element onLockedLayer = AddElement(scene, "Caption", LockedLayer);
        var layer = new TimelineLayer { ZIndex = 1 };
        scene.Layers.Add(layer);
        scene.Layers.Add(new TimelineLayer { ZIndex = LockedLayer, IsLocked = true });
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: new JsonObject
            {
                ["Elements"] = new JsonArray(
                    new JsonObject { ["Id"] = element.Id.ToString(), [nameof(Element.IsLocked)] = true },
                    new JsonObject { ["Id"] = onLockedLayer.Id.ToString(), [nameof(Element.IsLocked)] = true }),
                ["Layers"] = new JsonArray(
                    new JsonObject { ["Id"] = layer.Id.ToString(), [nameof(TimelineLayer.IsLocked)] = true }),
            },
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(element.IsLocked, Is.True);
            Assert.That(onLockedLayer.IsLocked, Is.True);
            Assert.That(scene.IsLayerLocked(1), Is.True);
        });
    }

    [TestCase("element")]
    [TestCase("layer")]
    [TestCase("remove-layer")]
    [TestCase("move-layer")]
    public void Apply_edit_refuses_to_unlock_an_element_or_a_layer(string target)
    {
        Scene scene = CreateScene();
        Element element = AddElement(scene, "Title", zIndex: 0);
        element.IsLocked = true;
        var layer = new TimelineLayer { ZIndex = LockedLayer, IsLocked = true };
        scene.Layers.Add(layer);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);
        string before = session.Documents.Read(scene).ToJsonString();

        JsonObject patch = target switch
        {
            "element" => ElementPatch(element, new JsonObject { [nameof(Element.IsLocked)] = false }),
            "layer" => LayerPatch(layer, new JsonObject { [nameof(TimelineLayer.IsLocked)] = false }),
            "remove-layer" => LayerPatch(layer, new JsonObject { ["$delete"] = true }),
            _ => LayerPatch(layer, new JsonObject { [nameof(TimelineLayer.ZIndex)] = 3 }),
        };

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error.Message, Does.Contain("cannot").And.Contain("unlock"));
            Assert.That(session.Documents.Read(scene).ToJsonString(), Is.EqualTo(before));
            Assert.That(scene.IsElementLocked(element), Is.True);
            Assert.That(scene.IsLayerLocked(LockedLayer), Is.True);
        });
    }

    [Test]
    public void Apply_edit_keeps_editing_unlocked_content_beside_locks()
    {
        Scene scene = CreateScene();
        Element unlocked = AddElement(scene, "Intro", zIndex: 0);
        Element locked = AddElement(scene, "Outro", zIndex: 1);
        locked.IsLocked = true;
        var layer = new TimelineLayer { ZIndex = LockedLayer, IsLocked = true };
        scene.Layers.Add(layer);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);

        // Deleting the first element shifts the locked one in the Elements array, and a locked layer
        // keeps its name editable, as in the timeline.
        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: new JsonObject
            {
                ["Elements"] = new JsonArray(new JsonObject { ["Id"] = unlocked.Id.ToString(), ["$delete"] = true }),
                ["Layers"] = new JsonArray(new JsonObject { ["Id"] = layer.Id.ToString(), ["Name"] = "Music" }),
            },
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(scene.Children, Is.EqualTo(new[] { locked }));
            Assert.That(scene.Layers.Single().Name, Is.EqualTo("Music"));
            Assert.That(scene.IsLayerLocked(LockedLayer), Is.True);
        });
    }

    [Test]
    public void Apply_edit_rejects_a_full_document_that_drops_a_locked_element()
    {
        Scene scene = CreateScene();
        Element locked = AddElement(scene, "Title", zIndex: 0);
        locked.IsLocked = true;
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);
        JsonObject desired = session.Documents.Read(scene);
        desired.Remove("Elements");

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Message, Does.Contain($"({locked.Id}) is locked"));
            Assert.That(scene.Children, Is.EqualTo(new[] { locked }));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Duplicate_object_rejects_an_object_in_a_locked_element(bool wrapInGroup)
    {
        Scene scene = CreateScene();
        Element element = AddElement(scene, "Title", zIndex: 0);
        var rect = new RectShape { Name = "Box" };
        element.AddObject(rect);
        element.IsLocked = true;
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);

        ToolResult<DuplicateObjectResponse> result = tools.DuplicateObject(rect.Id.ToString(), wrapInGroup: wrapInGroup);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error.Message, Does.Contain($"({element.Id}) is locked"));
            Assert.That(element.Objects, Is.EqualTo(new[] { rect }));
        });
    }

    private static EditTools CreateTools(IEditingSession session)
    {
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        return new EditTools(manager);
    }

    private static JsonObject ElementPatch(Element element, JsonObject members)
    {
        members["Id"] = element.Id.ToString();
        return new JsonObject { ["Elements"] = new JsonArray(members) };
    }

    private static JsonObject LayerPatch(TimelineLayer layer, JsonObject members)
    {
        members["Id"] = layer.Id.ToString();
        return new JsonObject { ["Layers"] = new JsonArray(members) };
    }

    private static JsonObject InsertPatch(string name, int zIndex)
    {
        return new JsonObject
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                ["$type"] = "[Beutl.ProjectSystem]:Element",
                [nameof(CoreObject.Name)] = name,
                [nameof(Element.Start)] = TimeSpan.Zero.ToString("c"),
                [nameof(Element.Length)] = TimeSpan.FromSeconds(2).ToString("c"),
                [nameof(Element.ZIndex)] = zIndex,
            })
        };
    }

    private static Element AddElement(Scene scene, string name, int zIndex)
    {
        var element = new Element
        {
            Name = name,
            Start = TimeSpan.FromSeconds(scene.Children.Count * 3),
            Length = TimeSpan.FromSeconds(2),
            ZIndex = zIndex,
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, $"{Guid.NewGuid():N}.belm"))
        };
        scene.Children.Add(element);
        return element;
    }

    private static Scene CreateScene()
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new Scene(1920, 1080, "Scene")
        {
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
    }
}
