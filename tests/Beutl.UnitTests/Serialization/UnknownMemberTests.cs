using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Engine.Expressions;
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
        shape.Width.SetKeyFrameAnimation(CreateAnimation(10f));
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
            Assert.That(restored.Width.GetKeyFrameAnimation(), Is.Not.Null);
            Assert.That(written[nameof(RectShape.Height)]!.GetValue<float>(), Is.EqualTo(42));
            Assert.That(written["FutureAmount"]!.GetValue<double>(), Is.EqualTo(0.5));
            Assert.That(JsonNode.DeepEquals(written["Animations"]!["FutureAmount"], futureAnimation), Is.True);
            Assert.That(written["Animations"]![nameof(RectShape.Width)], Is.Not.Null);
            Assert.That(JsonNode.DeepEquals(written["Expressions"]!["FutureAmount"], json["Expressions"]!["FutureAmount"]),
                Is.True);
        });
    }

    // An entry Deserialize does not read is kept even when it names a property this type has, as when a newer
    // version made a property animatable that is not animatable here.
    [Test]
    public void EngineObject_KeepsTheAnimationOfAPropertyThatIsNotAnimatableHere()
    {
        var shape = new RectShape();
        shape.Width.SetKeyFrameAnimation(CreateAnimation(10f));
        JsonNode animation = CoreSerializer.SerializeToJsonObject(shape)["Animations"]![nameof(RectShape.Width)]!
            .DeepClone();
        JsonObject json = CoreSerializer.SerializeToJsonObject(new SimpleHolder());
        if (json["Animations"] is not JsonObject animations)
        {
            json["Animations"] = animations = new JsonObject();
        }

        animations[nameof(SimpleHolder.Size)] = animation.DeepClone();

        var restored = (SimpleHolder)CoreSerializer.DeserializeFromJsonObject(json, typeof(SimpleHolder));
        JsonObject written = CoreSerializer.SerializeToJsonObject(restored);

        Assert.That(JsonNode.DeepEquals(written["Animations"]?[nameof(SimpleHolder.Size)], animation), Is.True,
            written.ToJsonString());
    }

    // What a property reads under a former name (FormerPersistedNameAttribute) is not unknown: its value, animation
    // and expression, and a CoreProperty value, are written under the current names only, and reading them keeps no
    // data this build does not know.
    [Test]
    public void MembersReadUnderAFormerName_AreNotKeptAsUnknown()
    {
        var source = new RenamedHolder { Title = "title" };
        source.Amount.CurrentValue = 3;
        source.Amount.SetKeyFrameAnimation(CreateAnimation(4f));
        source.Amount.Expression = Expression.Create<float>("5");
        JsonObject json = CoreSerializer.SerializeToJsonObject(source);
        RenameKey(json, nameof(RenamedHolder.Amount), RenamedHolder.FormerAmount);
        RenameKey(json["Animations"]!.AsObject(), nameof(RenamedHolder.Amount), RenamedHolder.FormerAmount);
        RenameKey(json["Expressions"]!.AsObject(), nameof(RenamedHolder.Amount), RenamedHolder.FormerAmount);
        RenameKey(json, nameof(RenamedHolder.Title), RenamedHolder.FormerTitle);
        var project = new Project();

        RenamedHolder restored;
        using (RootFileVersions.BeginReading(project))
        {
            restored = (RenamedHolder)CoreSerializer.DeserializeFromJsonObject(json, typeof(RenamedHolder));
        }

        JsonObject written = CoreSerializer.SerializeToJsonObject(restored);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Amount.CurrentValue, Is.EqualTo(3));
            Assert.That(restored.Amount.GetKeyFrameAnimation(), Is.Not.Null);
            Assert.That(restored.Amount.Expression?.ExpressionString, Is.EqualTo("5"));
            Assert.That(restored.Title, Is.EqualTo("title"));
            Assert.That(written.ContainsKey(RenamedHolder.FormerAmount), Is.False);
            Assert.That(written.ContainsKey(RenamedHolder.FormerTitle), Is.False);
            Assert.That(written["Animations"]!.AsObject().Select(static pair => pair.Key),
                Is.EqualTo(new[] { nameof(RenamedHolder.Amount) }));
            Assert.That(written["Expressions"]!.AsObject().Select(static pair => pair.Key),
                Is.EqualTo(new[] { nameof(RenamedHolder.Amount) }));
            Assert.That(restored.UnknownMembers, Is.Null);
            Assert.That(RootFileVersions.HasKeptUnknownData(project), Is.False);
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

    // A fallback writes back the type it names and the members it does not know, also as the root of a file.
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
            Assert.That(json.All(member => JsonNode.DeepEquals(written[member.Key], member.Value)), Is.True);
        });
    }

    // A fallback saves an edit of a member it knows, and writes back the members and expressions of the type it
    // stands in for as they were read.
    [Test]
    public void Fallback_SavesAnEditOfAMemberItKnows_AndWritesBackTheRest()
    {
        var json = new JsonObject
        {
            ["$type"] = "[Missing.Plugin]Missing.Namespace:MissingTransform",
            [nameof(CoreObject.Id)] = Guid.NewGuid().ToString(),
            ["Amount"] = 3,
            ["Expressions"] = new JsonObject { ["Amount"] = new JsonObject { ["Plugin"] = 1 } },
        };

        var fallback = (Transform)CoreSerializer.DeserializeFromJsonObject(json, typeof(Transform));
        fallback.IsEnabled = false;
        JsonObject written = CoreSerializer.SerializeToJsonObject(fallback);

        Assert.Multiple(() =>
        {
            Assert.That(written[nameof(EngineObject.IsEnabled)]!.GetValue<bool>(), Is.False);
            Assert.That(written["Amount"]!.GetValue<int>(), Is.EqualTo(3));
            Assert.That(JsonNode.DeepEquals(written["Expressions"], json["Expressions"]), Is.True);
            Assert.That(written["$type"]!.GetValue<string>(), Is.EqualTo(json["$type"]!.GetValue<string>()));
        });
    }

    // A fallback for an object whose data failed to load never read its members, so it saves the JSON it was given
    // as it is, not the defaults it holds.
    [Test]
    public void FailedFallback_SavesTheJsonItWasGivenAsItIs()
    {
        var json = new JsonObject
        {
            [nameof(CoreObject.Id)] = Guid.NewGuid().ToString(),
            [nameof(CoreObject.Name)] = "Kept",
            [nameof(EngineObject.IsEnabled)] = false,
        };
        json.WriteDiscriminator(typeof(UnreadableEngineObject));

        var fallback = (EngineObject)CoreSerializer.DeserializeFromJsonObject(json, typeof(EngineObject));
        fallback.Name = "Edited";
        JsonObject written = CoreSerializer.SerializeToJsonObject(fallback);

        Assert.Multiple(() =>
        {
            Assert.That(fallback, Is.InstanceOf<FallbackEngineObject>());
            Assert.That(((IFallback)fallback).Reason, Is.EqualTo(FallbackReason.DeserializationFailed));
            Assert.That(JsonNode.DeepEquals(written, json), Is.True);
        });
    }

    private static KeyFrameAnimation<float> CreateAnimation(float value)
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = value });
        return animation;
    }

    private static void RenameKey(JsonObject json, string from, string to)
    {
        JsonNode? node = json[from];
        json.Remove(from);
        json[to] = node;
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

    // An engine object with a property that is not animatable.
    [SuppressResourceClassGeneration]
    private sealed class SimpleHolder : EngineObject
    {
        public SimpleHolder() => ScanProperties<SimpleHolder>();

        public IExpressionProperty<float> Size { get; } = Property.CreateWithExpression(1f);
    }

    // An engine object whose properties were renamed: an IProperty and a CoreProperty, each with a former name.
    [SuppressResourceClassGeneration]
    private sealed class RenamedHolder : EngineObject
    {
        public const string FormerAmount = "FormerAmount";
        public const string FormerTitle = "FormerTitle";

        public static readonly CoreProperty<string?> TitleProperty;

        static RenamedHolder()
        {
            TitleProperty = ConfigureProperty<string?, RenamedHolder>(nameof(Title))
                .FormerPersistedName(FormerTitle)
                .Register();
        }

        public RenamedHolder() => ScanProperties<RenamedHolder>();

        [FormerPersistedName(FormerAmount)]
        public IExpressionProperty<float> Amount { get; } = Property.CreateAnimatable(1f);

        public string? Title
        {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
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

[SuppressResourceClassGeneration]
public sealed class UnreadableEngineObject : EngineObject
{
    public override void Deserialize(ICoreSerializationContext context) => throw new InvalidOperationException("Invalid payload.");
}
