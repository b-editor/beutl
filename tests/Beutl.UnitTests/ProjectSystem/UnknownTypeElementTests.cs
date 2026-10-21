using System.Text.Json.Nodes;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.ProjectSystem;

// An object whose type could not be loaded, usually because an extension is not installed, loads as a fallback
// that saves its known members and writes back the rest. Its element therefore stays editable and its edits are
// saved (b-editor/beutl#2855). Recovery keeps an element's file only when saving would lose part of it.
[TestFixture]
public class UnknownTypeElementTests
{
    private const string MissingShapeType = "[Missing.Plugin]Missing.Namespace:MissingShape";

    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "unknown-type-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // The scenario of b-editor/beutl#2855: an element holds an object from a plugin that is not installed, and a
    // member a newer version wrote. Editing the rest of the element is saved, and so are both of those, unchanged.
    [Test]
    public void ElementWithAMissingTypeAndAnUnknownMember_StaysEditable_AndSavesEditsAroundThem()
    {
        var sceneUri = new Uri(Path.Combine(_root, "scene.scene"));
        string elementPath = Path.Combine(_root, "element.belm");
        var scene = new Scene(64, 64, "Scene") { Uri = sceneUri, Duration = TimeSpan.FromSeconds(10) };
        var element = new Element { Length = TimeSpan.FromSeconds(1), Uri = new Uri(elementPath) };
        element.AddObject(new RectShape { Width = { CurrentValue = 10 } });
        element.AddObject(new RectShape());
        scene.Children.Add(element);
        CoreSerializer.StoreToUri(scene, sceneUri);
        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        JsonArray objects = json[nameof(Element.Objects)]!.AsArray();
        json["FutureClip"] = new JsonObject { ["Mode"] = "Ripple" };
        objects[0]!["FutureShape"] = 3;
        objects[1]!["$type"] = MissingShapeType;
        objects[1]!["PluginValue"] = new JsonArray(1, 2, 3);
        JsonNode unavailableJson = objects[1]!.DeepClone();
        File.WriteAllText(elementPath, json.ToJsonString());

        Scene restored = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element restoredElement = restored.Children.Single();
        var shape = (RectShape)restoredElement.Objects[0];
        using var harness = new HistoryHarness(restored);
        var resize = new ElementResizeService(harness.History);
        resize.Resize(restored,
            [new ElementResizeRequest(restoredElement, TimeSpan.FromSeconds(2), restoredElement.Length, restoredElement.ZIndex)],
            ripple: false);
        shape.Width.CurrentValue = 20;
        harness.History.Commit("Edit around the unavailable object");
        CoreSerializer.StoreToUri(restored, sceneUri);

        JsonObject saved = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        JsonArray savedObjects = saved[nameof(Element.Objects)]!.AsArray();
        Element reopened = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
        // The object whose type is missing moved with its element, which is a member it knows.
        unavailableJson["Start"] = "00:00:02";

        Assert.Multiple(() =>
        {
            Assert.That(restoredElement.Objects[1], Is.InstanceOf<IFallback>());
            Assert.That(restoredElement.SuppressedStorageSource, Is.Null);
            Assert.That(ElementEditability.IsEditable(restoredElement), Is.True);
            Assert.That(reopened.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(((RectShape)reopened.Objects[0]).Width.CurrentValue, Is.EqualTo(20));
            Assert.That(JsonNode.DeepEquals(savedObjects[1], unavailableJson), Is.True,
                "The rest of the object whose type is missing is saved as it was read.");
            Assert.That(JsonNode.DeepEquals(saved["FutureClip"], json["FutureClip"]), Is.True);
            Assert.That(savedObjects[0]!["FutureShape"]!.GetValue<int>(), Is.EqualTo(3));
            Assert.That(reopened.Objects[1], Is.InstanceOf<IFallback>());
        });
    }

    // Recovery keeps an element's file only when saving the element would lose part of what it was read with.
    [Test]
    public void RecoveryKeepsTheFile_OnlyWhenSavingWouldLoseSomething()
    {
        var sceneUri = new Uri(Path.Combine(_root, "scene.scene"));
        var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
        string[] names = ["missing-type", "unreadable-easing", "unparsable", "dropped-fallback", "node-tree", "unknown-operator"];
        foreach (string name in names)
        {
            var element = new Element
            {
                Name = name,
                Length = TimeSpan.FromSeconds(1),
                Uri = new Uri(Path.Combine(_root, $"{name}.belm")),
            };
            element.AddObject(name == "dropped-fallback" ? new DroppingHolder() : new RectShape());
            scene.Children.Add(element);
        }

        CoreSerializer.StoreToUri(scene, sceneUri);
        RewriteElement("missing-type", json => json[nameof(Element.Objects)]![0]!["$type"] = MissingShapeType);
        RewriteElement("unreadable-easing", json =>
            RecoveryBlockers.AddUnreadableEasing(json[nameof(Element.Objects)]![0]!.AsObject()));
        File.WriteAllText(Path.Combine(_root, "unparsable.belm"), "{ this is not JSON");
        RewriteElement("dropped-fallback", json =>
            json[nameof(Element.Objects)]![0]![DroppingHolder.LostKey] = new JsonObject { ["$type"] = MissingShapeType });
        File.WriteAllText(Path.Combine(_root, "node-tree.belm"), LegacyElement(
            useNode: true,
            operators: new JsonArray(),
            nodes: new JsonArray(new JsonObject { ["Id"] = Guid.NewGuid().ToString(), ["Name"] = "" })).ToJsonString());
        File.WriteAllText(Path.Combine(_root, "unknown-operator.belm"), LegacyElement(
            useNode: false,
            operators: new JsonArray(new JsonObject
            {
                ["$type"] = "[Missing.Plugin].Source:MissingOperator",
                ["Value"] = new JsonObject { ["$type"] = MissingShapeType, ["Size"] = 4 },
            }),
            nodes: new JsonArray()).ToJsonString());

        Scene restored = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        bool Kept(string name) => restored.Children
            .Single(element => Path.GetFileNameWithoutExtension(element.Uri!.LocalPath) == name)
            .SuppressedStorageSource is not null;

        Assert.Multiple(() =>
        {
            Assert.That(Kept("missing-type"), Is.False, "A fallback saves the JSON it was read from.");
            Assert.That(Kept("unreadable-easing"), Is.True, "An incident without a fallback has nothing to save it again.");
            Assert.That(Kept("unparsable"), Is.True);
            Assert.That(Kept("dropped-fallback"), Is.True, "A fallback that saving does not write would be lost.");
            Assert.That(Kept("node-tree"), Is.True, "A Beutl 1.x node tree was not migrated.");
            Assert.That(Kept("unknown-operator"), Is.True, "The migration keeps only the Value of an operator it does not know.");
        });
    }

    // A copy of an element must not share Ids with the original, including those of objects that a fallback or an
    // unknown member keeps as JSON, where no object in the graph stands for them.
    [Test]
    public void DuplicatingAnElement_RenewsTheIdsKeptAsJson()
    {
        Guid nestedId = Guid.NewGuid();
        Guid unknownId = Guid.NewGuid();
        var sceneUri = new Uri(Path.Combine(_root, "scene.scene"));
        string elementPath = Path.Combine(_root, "element.belm");
        var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
        var element = new Element { Length = TimeSpan.FromSeconds(1), Uri = new Uri(elementPath) };
        element.AddObject(new RectShape());
        scene.Children.Add(element);
        CoreSerializer.StoreToUri(scene, sceneUri);
        RewriteElement("element", json =>
        {
            JsonObject shape = json[nameof(Element.Objects)]![0]!.AsObject();
            shape["$type"] = MissingShapeType;
            shape["Child"] = new JsonObject { [nameof(CoreObject.Id)] = nestedId.ToString() };
            shape["Target"] = nestedId.ToString();
            json["FutureClip"] = new JsonObject { [nameof(CoreObject.Id)] = unknownId.ToString() };
        });
        Element original = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
        var originalFallback = (IFallback)original.Objects.Single();

        ObjectRegenerator.Regenerate(original, out Element copy);
        var copiedFallback = (IFallback)copy.Objects.Single();
        string copiedNestedId = copiedFallback.Json!["Child"]![nameof(CoreObject.Id)]!.GetValue<string>();
        JsonObject copiedJson = CoreSerializer.SerializeToJsonObject(copy);

        Assert.Multiple(() =>
        {
            Assert.That(((CoreObject)copiedFallback).Id, Is.Not.EqualTo(((CoreObject)originalFallback).Id));
            Assert.That(copiedFallback.Json![nameof(CoreObject.Id)]!.GetValue<string>(),
                Is.EqualTo(((CoreObject)copiedFallback).Id.ToString()));
            Assert.That(copiedNestedId, Is.Not.EqualTo(nestedId.ToString()));
            Assert.That(copiedFallback.Json!["Target"]!.GetValue<string>(), Is.EqualTo(copiedNestedId),
                "A reference inside the copy follows the Id it was given.");
            Assert.That(copiedJson["FutureClip"]![nameof(CoreObject.Id)]!.GetValue<string>(),
                Is.Not.EqualTo(unknownId.ToString()));
            Assert.That(originalFallback.Json!["Child"]![nameof(CoreObject.Id)]!.GetValue<string>(),
                Is.EqualTo(nestedId.ToString()));
        });
    }

    private void RewriteElement(string name, Action<JsonObject> rewrite)
    {
        string path = Path.Combine(_root, $"{name}.belm");
        JsonObject json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        rewrite(json);
        File.WriteAllText(path, json.ToJsonString());
    }

    // An element as Beutl 1.x wrote it: operators under Operation, and a node tree drawn when UseNode is on.
    private static JsonObject LegacyElement(bool useNode, JsonArray operators, JsonArray nodes) => new()
    {
        [nameof(CoreObject.Id)] = Guid.NewGuid().ToString(),
        [nameof(CoreObject.Name)] = "",
        [nameof(Element.Start)] = "00:00:02",
        [nameof(Element.Length)] = "00:00:01",
        [nameof(Element.ZIndex)] = 1,
        ["UseNode"] = useNode,
        ["Operation"] = new JsonObject { ["Children"] = operators },
        ["NodeTree"] = new JsonObject { ["Nodes"] = nodes },
    };

    // Reads a transform it then drops, as a type might that reads a value it does not keep. A fallback for that
    // transform would not be saved.
    [SuppressResourceClassGeneration]
    public sealed class DroppingHolder : EngineObject
    {
        public const string LostKey = "Lost";

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            _ = context.GetValue<Transform>(LostKey);
        }
    }
}
