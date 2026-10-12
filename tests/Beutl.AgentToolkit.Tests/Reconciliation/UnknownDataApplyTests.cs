using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

// An element that holds an object whose type could not be loaded, and members Beutl does not know, stays editable
// for an agent as for the editor. Documents leave the unknown members out and show the object as the JSON it keeps,
// and applying a document keeps both.
public sealed class UnknownDataApplyTests
{
    private const string MissingShapeType = "[Missing.Plugin]Missing.Namespace:MissingShape";

    [Test]
    public void Apply_edit_around_a_fallback_and_unknown_members_keeps_them()
    {
        (Scene scene, string elementPath, JsonObject written) = CreateScene();
        Element element = scene.Children.Single();
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);
        JsonObject document = session.Documents.Read(scene);
        JsonObject documentElement = document["Elements"]!.AsArray()[0]!.AsObject();
        documentElement[nameof(Element.Start)] = TimeSpan.FromSeconds(1).ToString("c");
        documentElement[nameof(Element.Objects)]![0]![nameof(RectShape.Width)] = 50;

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(desired: document, schemaVersion: SchemaVersion.Current);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        JsonObject saved = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        JsonArray writtenObjects = written[nameof(Element.Objects)]!.AsArray();
        JsonArray savedObjects = saved[nameof(Element.Objects)]!.AsArray();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(documentElement.ContainsKey("FutureClip"), Is.False, "Documents leave out unknown members.");
            Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(savedObjects[0]![nameof(RectShape.Width)]!.GetValue<float>(), Is.EqualTo(50));
            Assert.That(JsonNode.DeepEquals(saved["FutureClip"], written["FutureClip"]), Is.True);
            Assert.That(savedObjects[0]!["FutureShape"]!.GetValue<int>(), Is.EqualTo(3));
            Assert.That(JsonNode.DeepEquals(savedObjects[1], writtenObjects[1]), Is.True,
                "The object whose type is missing is saved as it was read.");
        });
    }

    // There is nothing Beutl knows in a fallback to apply, so an agent can leave it as it is or remove it.
    [Test]
    public void Apply_edit_that_changes_a_fallback_is_rejected()
    {
        (Scene scene, _, _) = CreateScene();
        using var session = new AgentToolkitTestSession(scene);
        JsonObject document = session.Documents.Read(scene);
        document["Elements"]![0]![nameof(Element.Objects)]![1]!["PluginValue"] = 7;

        var exception = Assert.Throws<ReconcileException>(() => new Reconciler().Apply(session, document));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(exception.Error.Message, Does.Not.Contain("Missing"),
                "The type name comes from the file, so the message leaves it out.");
            Assert.That(((IFallback)scene.Children.Single().Objects[1]).Json!["PluginValue"]!.GetValue<int>(), Is.EqualTo(1));
        });
    }

    // Saves a scene whose element holds a rectangle and a second object, then gives the element and the rectangle a
    // member Beutl does not know and the second object a type from a plugin that is not installed, with animations,
    // expressions and a value of its own.
    private static (Scene Scene, string ElementPath, JsonObject Written) CreateScene()
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var sceneUri = new Uri(Path.Combine(dir, "Scene.scene"));
        string elementPath = Path.Combine(dir, "element.belm");
        var scene = new Scene(1920, 1080, "Scene") { Uri = sceneUri, Duration = TimeSpan.FromSeconds(10) };
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(4),
            Uri = new Uri(elementPath),
        };
        element.AddObject(new RectShape { Width = { CurrentValue = 10 } });
        var plugin = new RectShape();
        plugin.Width.Animation = new Beutl.Animation.KeyFrameAnimation<float>
        {
            KeyFrames = { new Beutl.Animation.KeyFrame<float> { Value = 5 } },
        };
        element.AddObject(plugin);
        scene.Children.Add(element);
        CoreSerializer.StoreToUri(scene, sceneUri);

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        JsonArray objects = json[nameof(Element.Objects)]!.AsArray();
        json["FutureClip"] = new JsonObject { ["Mode"] = "Ripple" };
        objects[0]!["FutureShape"] = 3;
        objects[1]!["$type"] = MissingShapeType;
        objects[1]!["PluginValue"] = 1;
        File.WriteAllText(elementPath, json.ToJsonString());
        return (CoreSerializer.RestoreFromUri<Scene>(sceneUri), elementPath, json);
    }
}
