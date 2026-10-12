using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.Serialization;

// Reading an object keeps the members of its JSON that it does not read, and writing it writes them back, so data
// that a newer version of Beutl or of an extension wrote survives an older one (b-editor/beutl#2875).
public class UnknownMemberTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "unknown-members-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void CoreObject_KeepsTheMembersItDoesNotRead_AndWritesThemBackAfterItsOwn()
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(new PlainObject { Value = 1 });
        json["Future"] = new JsonObject { ["Nested"] = new JsonArray(1, 2) };
        json["Later"] = "text";

        var restored = (PlainObject)CoreSerializer.DeserializeFromJsonObject(json, typeof(PlainObject));
        restored.Value = 2;
        JsonObject written = CoreSerializer.SerializeToJsonObject(restored);
        string[] keys = written.Select(static pair => pair.Key).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(written[nameof(PlainObject.Value)]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(JsonNode.DeepEquals(written["Future"], json["Future"]), Is.True);
            Assert.That(written["Later"]!.GetValue<string>(), Is.EqualTo("text"));
            Assert.That(Array.IndexOf(keys, "Future"), Is.GreaterThan(Array.IndexOf(keys, nameof(PlainObject.Value))));
        });
    }

    // A new property of an engine object comes with its value, its animation and its expression; an older version
    // keeps all three.
    [Test]
    public void EngineObject_KeepsUnknownMembersAndTheAnimationsAndExpressionsOfPropertiesItDoesNotHave()
    {
        var shape = new RectShape();
        shape.Width.Animation = CreateAnimation(10f);
        JsonObject json = CoreSerializer.SerializeToJsonObject(shape);
        JsonObject futureAnimation = json["Animations"]![nameof(RectShape.Width)]!.DeepClone().AsObject();
        json["FutureAmount"] = 0.5;
        json["Animations"]!["FutureAmount"] = futureAnimation;
        json["Expressions"]!["FutureAmount"] = new JsonObject { ["Source"] = "1 + 2" };

        var restored = (RectShape)CoreSerializer.DeserializeFromJsonObject(json, typeof(EngineObject));
        restored.Height.CurrentValue = 42;
        JsonObject written = CoreSerializer.SerializeToJsonObject(restored);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Width.Animation, Is.Not.Null);
            Assert.That(written[nameof(RectShape.Height)]!.GetValue<float>(), Is.EqualTo(42));
            Assert.That(written["FutureAmount"]!.GetValue<double>(), Is.EqualTo(0.5));
            Assert.That(JsonNode.DeepEquals(written["Animations"]!["FutureAmount"], futureAnimation), Is.True);
            Assert.That(written["Animations"]![nameof(RectShape.Width)], Is.Not.Null);
            Assert.That(JsonNode.DeepEquals(written["Expressions"]!["FutureAmount"], json["Expressions"]!["FutureAmount"]),
                Is.True);
        });
    }

    // The element, an object in it and a brush in that object each keep what they do not read, through a save.
    [Test]
    public void UnknownMembersOfNestedObjects_SurviveASaveOfTheirElementFile()
    {
        var sceneUri = new Uri(Path.Combine(_root, "scene.scene"));
        string elementPath = Path.Combine(_root, "element.belm");
        var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
        var element = new Element { Length = TimeSpan.FromSeconds(1), Uri = new Uri(elementPath) };
        element.AddObject(new RectShape { Fill = { CurrentValue = new SolidColorBrush(Colors.Red) } });
        scene.Children.Add(element);
        CoreSerializer.StoreToUri(scene, sceneUri);
        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        JsonObject shapeJson = json[nameof(Element.Objects)]![0]!.AsObject();
        json["FutureClip"] = new JsonObject { ["Mode"] = "Ripple" };
        shapeJson["FutureShape"] = 3;
        shapeJson[nameof(RectShape.Fill)]!["FutureBrush"] = new JsonArray("a", "b");
        File.WriteAllText(elementPath, json.ToJsonString());

        Scene restored = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element restoredElement = restored.Children.Single();
        restoredElement.Start = TimeSpan.FromSeconds(2);
        CoreSerializer.StoreToUri(restored, sceneUri);
        JsonObject saved = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        JsonObject savedShape = saved[nameof(Element.Objects)]![0]!.AsObject();

        Assert.Multiple(() =>
        {
            Assert.That(restoredElement.SuppressedStorageSource, Is.Null);
            Assert.That(CoreSerializer.RestoreFromUri<Element>(new Uri(elementPath)).Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(JsonNode.DeepEquals(saved["FutureClip"], json["FutureClip"]), Is.True);
            Assert.That(savedShape["FutureShape"]!.GetValue<int>(), Is.EqualTo(3));
            Assert.That(JsonNode.DeepEquals(savedShape[nameof(RectShape.Fill)]!["FutureBrush"],
                shapeJson[nameof(RectShape.Fill)]!["FutureBrush"]), Is.True);
        });
    }

    // A type lists the keys it drops, for data it has replaced or no longer needs; a derived type drops them too.
    [Test]
    public void DroppedMembers_AreNotKept_ByTheTypeOrTypesDerivedFromIt()
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(new DerivedDroppingObject());
        json["Retired"] = 1;
        json["Future"] = 2;

        var restored = (DerivedDroppingObject)CoreSerializer.DeserializeFromJsonObject(json, typeof(DerivedDroppingObject));
        JsonObject written = CoreSerializer.SerializeToJsonObject(restored);

        Assert.Multiple(() =>
        {
            Assert.That(written.ContainsKey("Retired"), Is.False);
            Assert.That(written["Future"]!.GetValue<int>(), Is.EqualTo(2));
        });
    }

    // Discriminators and other keys that start with '$', the legacy "@type", the Uri of an embedded reference and the
    // version fields of a root file are never members.
    [Test]
    public void ReservedKeys_AreNeverKept()
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(new PlainObject());
        json["@type"] = "[Beutl.Core]Beutl:Legacy";
        json["$id"] = "1";
        json["Uri"] = new Uri(Path.Combine(_root, "embedded.json")).ToString();
        json[FileFormat.AppVersionKey] = "9.0.0";
        json[FileFormat.MinAppVersionKey] = "9.0.0";

        var restored = (PlainObject)CoreSerializer.DeserializeFromJsonObject(json, typeof(PlainObject));

        Assert.That(restored.UnknownMembers, Is.Null);
    }

    // A populate that leaves a member out keeps it, as it keeps the value of a known property it leaves out, and a
    // member it holds replaces the one kept before.
    [Test]
    public void PopulatingAgain_KeepsTheMembersItLeavesOut()
    {
        var obj = new PlainObject();
        CoreSerializer.PopulateFromJsonObject(obj, new JsonObject { ["Future"] = 1 });
        CoreSerializer.PopulateFromJsonObject(obj, new JsonObject { ["Later"] = 2 });
        CoreSerializer.PopulateFromJsonObject(obj, new JsonObject { ["Future"] = 3 });

        JsonObject written = CoreSerializer.SerializeToJsonObject(obj);

        Assert.Multiple(() =>
        {
            Assert.That(written["Future"]!.GetValue<int>(), Is.EqualTo(3));
            Assert.That(written["Later"]!.GetValue<int>(), Is.EqualTo(2));
        });
    }

    // A type that reads the JSON object itself can read any key, so none counts as unknown.
    [Test]
    public void ReadingTheJsonObjectItself_KeepsNothing()
    {
        var obj = new RawReadingObject();
        CoreSerializer.PopulateFromJsonObject(obj, new JsonObject { ["Legacy"] = 1, ["Future"] = 2 });

        Assert.That(obj.UnknownMembers, Is.Null);
    }

    // The Beutl 1.x migration and agent documents read and write without unknown members.
    [Test]
    public void KeepUnknownMembersFalse_DropsThemOnReadAndLeavesThemOutOnWrite()
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(new PlainObject());
        json["Future"] = 1;
        var options = new CoreSerializerOptions { KeepUnknownMembers = false };

        var dropped = (PlainObject)CoreSerializer.DeserializeFromJsonObject(json, typeof(PlainObject), options);
        var kept = (PlainObject)CoreSerializer.DeserializeFromJsonObject(json, typeof(PlainObject));
        JsonObject withoutThem = CoreSerializer.SerializeToJsonObject(kept, options);

        Assert.Multiple(() =>
        {
            Assert.That(dropped.UnknownMembers, Is.Null);
            Assert.That(kept.UnknownMembers, Is.Not.Null);
            Assert.That(withoutThem.ContainsKey("Future"), Is.False);
        });
    }

    // A fallback saves the JSON it was read from, the type it names included, also as the root of a file.
    [Test]
    public void Fallback_KeepsTheTypeItStandsInFor_WhenWrittenAsTheRoot()
    {
        var json = new JsonObject
        {
            ["$type"] = "[Missing.Plugin]Missing.Namespace:MissingTransform",
            [nameof(CoreObject.Id)] = Guid.NewGuid().ToString(),
            ["Amount"] = 3,
        };

        var fallback = (Transform)CoreSerializer.DeserializeFromJsonObject(json, typeof(Transform));
        JsonObject written = CoreSerializer.SerializeToJsonObject(fallback);

        Assert.Multiple(() =>
        {
            Assert.That(fallback, Is.InstanceOf<FallbackTransform>());
            Assert.That(fallback.Id.ToString(), Is.EqualTo(json[nameof(CoreObject.Id)]!.GetValue<string>()));
            Assert.That(JsonNode.DeepEquals(written, json), Is.True);
        });
    }

    private static KeyFrameAnimation<float> CreateAnimation(float value)
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = value });
        return animation;
    }

    private sealed class PlainObject : CoreObject
    {
        public static readonly CoreProperty<int> ValueProperty;

        static PlainObject()
        {
            ValueProperty = ConfigureProperty<int, PlainObject>(nameof(Value)).Register();
        }

        public int Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }

    [DroppedMembers("Retired")]
    private class DroppingObject : CoreObject;

    private sealed class DerivedDroppingObject : DroppingObject;

    private sealed class RawReadingObject : CoreObject
    {
        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            _ = ((IJsonSerializationContext)context).GetJsonObject()["Legacy"];
        }
    }
}
