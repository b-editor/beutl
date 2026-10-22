using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Animation;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

// An element that holds an object whose type could not be loaded, and members Beutl does not know, stays editable
// for an agent as for the editor. Documents leave the unknown members out and show the object as the JSON it keeps,
// and applying a document keeps both, but adds no member Beutl does not know.
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
        // The object whose type is missing moved with its element, which is a member it knows.
        JsonNode unavailable = writtenObjects[1]!.DeepClone();
        unavailable[nameof(Element.Start)] = TimeSpan.FromSeconds(1).ToString("c");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(documentElement.ContainsKey("FutureClip"), Is.False, "Documents leave out unknown members.");
            Assert.That(element.Start, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(savedObjects[0]![nameof(RectShape.Width)]!.GetValue<float>(), Is.EqualTo(50));
            Assert.That(JsonNode.DeepEquals(saved["FutureClip"], written["FutureClip"]), Is.True);
            Assert.That(savedObjects[0]!["FutureShape"]!.GetValue<int>(), Is.EqualTo(3));
            Assert.That(savedObjects[0]![nameof(Shape.Fill)]!["FutureBrush"]!.GetValue<int>(), Is.EqualTo(4));
            Assert.That(JsonNode.DeepEquals(savedObjects[1], unavailable), Is.True,
                "The rest of the object whose type is missing is saved as it was read.");
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

    // The document leaves out the members the rectangle's brush kept, so the brush must still count as unchanged.
    [Test]
    public void Apply_edit_keeps_the_unknown_members_of_a_nested_value_it_leaves_unchanged()
    {
        (Scene scene, string elementPath, _) = CreateScene();
        using var session = new AgentToolkitTestSession(scene);
        JsonObject document = session.Documents.Read(scene);
        document["Elements"]![0]![nameof(Element.Objects)]![0]![nameof(RectShape.Width)] = 50;

        new Reconciler().Apply(session, document);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        JsonObject saved = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        JsonObject savedShape = saved[nameof(Element.Objects)]![0]!.AsObject();

        Assert.Multiple(() =>
        {
            Assert.That(savedShape[nameof(RectShape.Width)]!.GetValue<float>(), Is.EqualTo(50));
            Assert.That(savedShape[nameof(Shape.Fill)]!["FutureBrush"]!.GetValue<int>(), Is.EqualTo(4));
        });
    }

    // A nested value the document changes is applied in place, as in the editor, so it keeps its unknown members.
    [Test]
    public void Apply_edit_changes_a_nested_value_in_place_and_keeps_its_unknown_members()
    {
        (Scene scene, string elementPath, _) = CreateScene();
        var shape = (RectShape)scene.Children.Single().Objects[0];
        var brush = (SolidColorBrush)shape.Fill.Value!;
        using var session = new AgentToolkitTestSession(scene);
        JsonObject document = session.Documents.Read(scene);
        document["Elements"]![0]![nameof(Element.Objects)]![0]![nameof(Shape.Fill)]![nameof(SolidColorBrush.Color)] =
            CoreSerializer.SerializeToJsonNode(Colors.Red);

        new Reconciler().Apply(session, document);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        JsonObject savedFill = ReadSavedFill(elementPath);

        Assert.Multiple(() =>
        {
            Assert.That(shape.Fill.Value, Is.SameAs(brush));
            Assert.That(brush.Color.Value, Is.EqualTo(Colors.Red));
            Assert.That(savedFill["FutureBrush"]?.GetValue<int>(), Is.EqualTo(4));
        });
    }

    // The same holds at every depth, whether the edit changes the brush's transform or only the brush.
    [TestCase(false)]
    [TestCase(true)]
    public void Apply_edit_keeps_the_unknown_members_inside_a_nested_value_it_changes(bool changeTransform)
    {
        (Scene scene, string elementPath) = CreateSceneWithABrushTransform();
        var brush = (SolidColorBrush)((RectShape)scene.Children.Single().Objects[0]).Fill.Value!;
        var transform = (TranslateTransform)brush.Transform.Value!;
        using var session = new AgentToolkitTestSession(scene);
        JsonObject document = session.Documents.Read(scene);
        JsonNode fill = document["Elements"]![0]![nameof(Element.Objects)]![0]![nameof(Shape.Fill)]!;
        fill[nameof(Brush.Opacity)] = 50;
        if (changeTransform)
        {
            fill[nameof(Brush.Transform)]![nameof(TranslateTransform.X)] = 20;
        }

        new Reconciler().Apply(session, document);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        JsonObject savedFill = ReadSavedFill(elementPath);

        Assert.Multiple(() =>
        {
            Assert.That(brush.Opacity.Value, Is.EqualTo(50));
            Assert.That(brush.Transform.Value, Is.SameAs(transform));
            Assert.That(transform.X.Value, Is.EqualTo(changeTransform ? 20 : 10));
            Assert.That(savedFill["FutureBrush"]?.GetValue<int>(), Is.EqualTo(4));
            Assert.That(savedFill[nameof(Brush.Transform)]!["FutureTransform"]?.GetValue<int>(), Is.EqualTo(5));
        });
    }

    // The applier's rules remove an animation the document drops from a nested value it keeps in place.
    [Test]
    public void Apply_edit_removes_an_animation_from_a_nested_value_it_keeps()
    {
        (Scene scene, string elementPath, _) = CreateScene();
        var shape = (RectShape)scene.Children.Single().Objects[0];
        Brush brush = shape.Fill.Value!;
        var animation = new KeyFrameAnimation<float> { KeyFrames = { new KeyFrame<float> { Value = 20 } } };
        brush.Opacity.SetKeyFrameAnimation(animation);
        using var session = new AgentToolkitTestSession(scene);
        JsonObject document = session.Documents.Read(scene);
        document["Elements"]![0]![nameof(Element.Objects)]![0]![nameof(Shape.Fill)]!["Animations"]!.AsObject()
            .Remove(nameof(Brush.Opacity));

        new Reconciler().Apply(session, document);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        Scene reloaded = CoreSerializer.RestoreFromUri<Scene>(scene.Uri!);
        Brush reloadedBrush = ((RectShape)reloaded.Children.Single().Objects[0]).Fill.Value!;

        Assert.Multiple(() =>
        {
            Assert.That(shape.Fill.Value, Is.SameAs(brush));
            Assert.That(brush.Opacity.GetKeyFrameAnimation(), Is.Null);
            Assert.That(reloadedBrush.Opacity.GetKeyFrameAnimation(), Is.Null);
            Assert.That(ReadSavedFill(elementPath)["FutureBrush"]?.GetValue<int>(), Is.EqualTo(4));
        });
    }

    // Another type or Id still replaces the value. Written directly, as apply_edit refuses both documents.
    [TestCase(true)]
    [TestCase(false)]
    public void Writing_a_nested_value_of_another_type_or_Id_replaces_it(bool otherType)
    {
        (Scene scene, string elementPath, _) = CreateScene();
        var shape = (RectShape)scene.Children.Single().Objects[0];
        Brush brush = shape.Fill.Value!;
        using var session = new AgentToolkitTestSession(scene);
        JsonObject document = session.Documents.Read(scene);
        Brush replacement = otherType ? new LinearGradientBrush { Id = brush.Id } : new SolidColorBrush(Colors.Red);
        document["Elements"]![0]![nameof(Element.Objects)]![0]![nameof(Shape.Fill)] =
            CoreSerializer.SerializeToJsonObject(replacement);

        session.Documents.Write(scene, document);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        JsonObject savedFill = ReadSavedFill(elementPath);

        Assert.Multiple(() =>
        {
            Assert.That(shape.Fill.Value, Is.Not.SameAs(brush));
            Assert.That(shape.Fill.Value, Is.InstanceOf(replacement.GetType()));
            Assert.That(shape.Fill.Value!.Id, Is.EqualTo(replacement.Id));
            Assert.That(savedFill.ContainsKey("FutureBrush"), Is.False);
        });
    }

    // A key Beutl does not know that the agent adds to an element is dropped, as before objects kept unknown members
    // (validation already rejects one on an object of the element), while the ones the element kept from its file
    // stay.
    [Test]
    public void Apply_edit_drops_members_the_agent_adds_that_Beutl_does_not_know()
    {
        (Scene scene, string elementPath, JsonObject written) = CreateScene();
        using var session = new AgentToolkitTestSession(scene);
        JsonObject document = session.Documents.Read(scene);
        JsonObject documentElement = document["Elements"]![0]!.AsObject();
        documentElement["AgentClip"] = 1;
        documentElement[nameof(Element.Objects)]![0]![nameof(RectShape.Width)] = 50;

        new Reconciler().Apply(session, document);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        JsonObject saved = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();

        Assert.Multiple(() =>
        {
            Assert.That(saved.ContainsKey("AgentClip"), Is.False);
            Assert.That(JsonNode.DeepEquals(saved["FutureClip"], written["FutureClip"]), Is.True);
            Assert.That(saved[nameof(Element.Objects)]![0]!["FutureShape"]!.GetValue<int>(), Is.EqualTo(3));
        });
    }

    // The document gives an object stored in a file of its own the Uri of that file, which the JSON a fallback keeps
    // does not hold. An unchanged fallback of that kind lets the rest of its element be edited, and saving writes
    // its file back as it was read (only the formatting may differ).
    [Test]
    public void Apply_edit_around_a_fallback_stored_in_its_own_file_keeps_that_file()
    {
        (Scene scene, string elementPath, string sidecarPath) = CreateSceneWithAFallbackInItsOwnFile();
        JsonNode sidecar = JsonNode.Parse(File.ReadAllText(sidecarPath))!;
        using var session = new AgentToolkitTestSession(scene);
        JsonObject document = session.Documents.Read(scene);
        document["Elements"]![0]![nameof(Element.Objects)]![0]![nameof(RectShape.Width)] = 50;

        new Reconciler().Apply(session, document);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        JsonObject saved = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();

        Assert.Multiple(() =>
        {
            Assert.That(saved[nameof(Element.Objects)]![0]![nameof(RectShape.Width)]!.GetValue<float>(), Is.EqualTo(50));
            Assert.That(scene.Children.Single().Objects[1], Is.InstanceOf<IFallback>());
            Assert.That(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(sidecarPath)), sidecar), Is.True);
        });
    }

    // Moving such a fallback to another file would be an edit of it.
    [Test]
    public void Apply_edit_that_moves_a_fallback_to_another_file_is_rejected()
    {
        (Scene scene, _, string sidecarPath) = CreateSceneWithAFallbackInItsOwnFile();
        using var session = new AgentToolkitTestSession(scene);
        JsonObject document = session.Documents.Read(scene);
        document["Elements"]![0]![nameof(Element.Objects)]![1]!["Uri"] = "elsewhere.json";

        var exception = Assert.Throws<ReconcileException>(() => new Reconciler().Apply(session, document));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(((CoreObject)scene.Children.Single().Objects[1]).Uri, Is.EqualTo(new Uri(sidecarPath)));
        });
    }

    // Saves a scene whose element holds a rectangle and a second object, then gives the element, the rectangle and
    // the rectangle's brush a member Beutl does not know and the second object a type from a plugin that is not
    // installed, with animations, expressions and a value of its own.
    private static (Scene Scene, string ElementPath, JsonObject Written) CreateScene()
    {
        string dir = TestWorkspace.CreateDirectory();
        var sceneUri = new Uri(Path.Combine(dir, "Scene.scene"));
        string elementPath = Path.Combine(dir, "element.belm");
        var scene = new Scene(1920, 1080, "Scene") { Uri = sceneUri, Duration = MediaTime.FromSeconds(10) };
        var element = new Element
        {
            Start = MediaTime.Zero,
            Length = MediaTime.FromSeconds(4),
            Uri = new Uri(elementPath),
        };
        element.AddObject(new RectShape { Width = { Value = 10 } });
        var plugin = new RectShape();
        plugin.Width.SetKeyFrameAnimation(new Beutl.Animation.KeyFrameAnimation<float>
        {
            KeyFrames = { new Beutl.Animation.KeyFrame<float> { Value = 5 } },
        });
        element.AddObject(plugin);
        scene.Children.Add(element);
        CoreSerializer.StoreToUri(scene, sceneUri);

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        JsonArray objects = json[nameof(Element.Objects)]!.AsArray();
        json["FutureClip"] = new JsonObject { ["Mode"] = "Ripple" };
        objects[0]!["FutureShape"] = 3;
        objects[0]![nameof(Shape.Fill)]!["FutureBrush"] = 4;
        objects[1]!["$type"] = MissingShapeType;
        objects[1]!["PluginValue"] = 1;
        File.WriteAllText(elementPath, json.ToJsonString());
        return (CoreSerializer.RestoreFromUri<Scene>(sceneUri), elementPath, json);
    }

    // Saves a scene whose element holds a rectangle and a second one stored in a file of its own, as a plugin may
    // store an object, then gives the second one a type from a plugin that is not installed.
    private static (Scene Scene, string ElementPath, string SidecarPath) CreateSceneWithAFallbackInItsOwnFile()
    {
        string dir = TestWorkspace.CreateDirectory();
        var sceneUri = new Uri(Path.Combine(dir, "Scene.scene"));
        string elementPath = Path.Combine(dir, "element.belm");
        string sidecarPath = Path.Combine(dir, "parts", "plugin.json");
        var scene = new Scene(1920, 1080, "Scene") { Uri = sceneUri, Duration = MediaTime.FromSeconds(10) };
        var element = new Element
        {
            Start = MediaTime.Zero,
            Length = MediaTime.FromSeconds(4),
            Uri = new Uri(elementPath),
        };
        element.AddObject(new RectShape { Width = { Value = 10 } });
        element.AddObject(new RectShape { Uri = new Uri(sidecarPath) });
        scene.Children.Add(element);
        CoreSerializer.StoreToUri(scene, sceneUri);

        JsonObject sidecar = JsonNode.Parse(File.ReadAllText(sidecarPath))!.AsObject();
        sidecar["$type"] = MissingShapeType;
        File.WriteAllText(sidecarPath, sidecar.ToJsonString());
        return (CoreSerializer.RestoreFromUri<Scene>(sceneUri), elementPath, sidecarPath);
    }

    // The scene of CreateScene, whose rectangle's brush also gets a transform with a member Beutl does not know.
    private static (Scene Scene, string ElementPath) CreateSceneWithABrushTransform()
    {
        (Scene scene, string elementPath, _) = CreateScene();
        ((RectShape)scene.Children.Single().Objects[0]).Fill.Value!.Transform.Value = new TranslateTransform(10, 0);
        CoreSerializer.StoreToUri(scene, scene.Uri!);

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        json[nameof(Element.Objects)]![0]![nameof(Shape.Fill)]![nameof(Brush.Transform)]!["FutureTransform"] = 5;
        File.WriteAllText(elementPath, json.ToJsonString());
        return (CoreSerializer.RestoreFromUri<Scene>(scene.Uri!), elementPath);
    }

    // The rectangle's brush as the element file holds it.
    private static JsonObject ReadSavedFill(string elementPath)
        => JsonNode.Parse(File.ReadAllText(elementPath))![nameof(Element.Objects)]![0]![nameof(Shape.Fill)]!.AsObject();
}
