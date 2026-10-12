using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.JsonDiscriminator;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.Serialization;

// A type or property renamed with FormerPersistedNameAttribute still loads from what was saved under the former
// name, and is written under its current name (b-editor/beutl#2875).
public class FormerPersistedNameTests
{
    // Each "former" name below is how $type wrote the type before the rename: the namespace relative to the assembly
    // name, then the declaring type and the type itself.
    private const string FormerLeafName = "[Beutl.UnitTests].Serialization:FormerPersistedNameTests:FormerLeaf";
    private const string FormerBoxName = "[Beutl.UnitTests].Serialization:FormerPersistedNameTests:FormerBox";
    private const string FormerUnloadedName = "[Beutl.UnitTests].Serialization:FormerPersistedNameTests:FormerUnloaded";

    [Test]
    public void A_renamed_type_loads_from_a_file_that_holds_its_former_name()
    {
        var element = new Element();
        element.Objects.Add(new RenamedLeaf { Width = { CurrentValue = 3 } });
        JsonObject json = CoreSerializer.SerializeToJsonObject(element);
        JsonObject item = json[nameof(Element.Objects)]!.AsArray()[0]!.AsObject();
        Assert.That((string?)item["$type"], Is.EqualTo(TypeFormat.ToString(typeof(RenamedLeaf))));
        item["$type"] = FormerLeafName;
        string path = Path.Combine(Path.GetTempPath(), $"beutl-former-name-{Guid.NewGuid():N}.belm");
        int fallbacksBefore = DeserializationIncidents.FallbackCount;
        try
        {
            File.WriteAllText(path, json.ToJsonString());

            Element restored = CoreSerializer.RestoreFromUri<Element>(UriHelper.CreateFromPath(path));
            JsonObject resaved = CoreSerializer.SerializeToJsonObject(restored);

            Assert.Multiple(() =>
            {
                Assert.That(restored.Objects.Single(), Is.TypeOf<RenamedLeaf>());
                Assert.That(((RenamedLeaf)restored.Objects[0]).Width.CurrentValue, Is.EqualTo(3));
                Assert.That(DeserializationIncidents.FallbackCount, Is.EqualTo(fallbacksBefore));
                Assert.That(
                    (string?)resaved[nameof(Element.Objects)]![0]!["$type"],
                    Is.EqualTo(TypeFormat.ToString(typeof(RenamedLeaf))),
                    "It is written under its current name.");
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    // $type writes a namespace that starts with the assembly name relative to it, but the other form names the
    // same type, and a type argument is a type name like any other.
    [Test]
    public void A_former_name_resolves_in_either_namespace_form_and_as_a_type_argument()
    {
        string absolute = "[Beutl.UnitTests]Beutl.UnitTests.Serialization:FormerPersistedNameTests:FormerLeaf";
        string optional = TypeFormat.ToString(typeof(Optional<RenamedLeaf>))
            .Replace(TypeFormat.ToString(typeof(RenamedLeaf)), FormerLeafName);

        Assert.Multiple(() =>
        {
            Assert.That(TypeFormat.ToType(FormerLeafName), Is.EqualTo(typeof(RenamedLeaf)));
            Assert.That(TypeFormat.ToType(absolute), Is.EqualTo(typeof(RenamedLeaf)));
            Assert.That(optional, Does.Contain(FormerLeafName));
            Assert.That(TypeFormat.ToType(optional), Is.EqualTo(typeof(Optional<RenamedLeaf>)));
        });
    }

    [Test]
    public void A_generic_type_resolves_its_former_name_with_any_type_arguments()
    {
        string name = $"{FormerBoxName}<{TypeFormat.ToString(typeof(int))}>";

        Assert.That(TypeFormat.ToType(name), Is.EqualTo(typeof(RenamedBox<int>)));
    }

    // A former name is read only where no type has the name now: Impostor gives CurrentHolder's name as its former
    // name.
    [Test]
    public void A_current_name_wins_over_a_former_name()
    {
        string name = TypeFormat.ToString(typeof(CurrentHolder));

        Assert.That(TypeFormat.ToType(name), Is.EqualTo(typeof(CurrentHolder)));
    }

    // Without the assembly, with type arguments, without the colon before the type, or empty.
    [Test]
    public void A_former_name_not_written_as_a_type_name_is_left_out()
    {
        IReadOnlyList<FormerTypeName> names = TypeFormat.GetFormerNames(typeof(BadlyNamed));

        Assert.That(names, Is.EqualTo(new[]
        {
            new FormerTypeName(
                "Beutl.UnitTests",
                "Beutl.UnitTests.Former",
                "BadlyNamed",
                "Beutl.UnitTests.Former.Outer+BadlyNamed"),
        }));
    }

    // A plugin's types are announced through TypeUnloadNotifier before its load context is unloaded. The names that
    // resolved to them are dropped from the cache, which would otherwise keep the context alive, and their former
    // names no longer resolve.
    [Test]
    public void Unloading_a_type_forgets_the_names_that_resolved_to_it()
    {
        Assert.That(TypeFormat.ToType(FormerUnloadedName), Is.EqualTo(typeof(UnloadedType)));

        TypeUnloadNotifier.NotifyUnloading([typeof(UnloadedType)]);

        Assert.That(TypeFormat.ToType(FormerUnloadedName), Is.Null);
    }

    [Test]
    public void An_engine_property_reads_its_value_animation_and_expression_under_a_former_key()
    {
        var source = new MemberHolder();
        source.Width.CurrentValue = 3;
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 32 });
        source.Width.Animation = animation;
        source.Width.Expression = Expression.Create<float>("5");
        source.Tags.Add("a");
        JsonObject json = CoreSerializer.SerializeToJsonObject(source);
        RenameKey(json, nameof(MemberHolder.Width), "FormerWidth");
        RenameKey(json["Animations"]!.AsObject(), nameof(MemberHolder.Width), "FormerWidth");
        RenameKey(json["Expressions"]!.AsObject(), nameof(MemberHolder.Width), "FormerWidth");
        RenameKey(json, nameof(MemberHolder.Tags), "FormerTags");

        var restored = (MemberHolder)CoreSerializer.DeserializeFromJsonObject(json, typeof(MemberHolder));
        JsonObject resaved = CoreSerializer.SerializeToJsonObject(restored);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Width.CurrentValue, Is.EqualTo(3));
            Assert.That(
                (restored.Width.Animation as KeyFrameAnimation<float>)?.KeyFrames.Select(keyFrame => keyFrame.Value),
                Is.EqualTo(new[] { 32f }));
            Assert.That(restored.Width.Expression?.ExpressionString, Is.EqualTo("5"));
            Assert.That(restored.Tags, Is.EqualTo(new[] { "a" }));
            Assert.That(resaved.ContainsKey(nameof(MemberHolder.Width)), Is.True, "It is written under its current key.");
            Assert.That(resaved.ContainsKey("FormerWidth"), Is.False);
            Assert.That(resaved["Animations"]?.AsObject().ContainsKey(nameof(MemberHolder.Width)), Is.True);
            Assert.That(resaved.ContainsKey("FormerTags"), Is.False);
        });
    }

    [Test]
    public void A_member_under_its_current_key_wins_over_a_former_key()
    {
        var source = new MemberHolder();
        source.Width.CurrentValue = 1;
        JsonObject json = CoreSerializer.SerializeToJsonObject(source);
        json["FormerWidth"] = 2f;

        var restored = (MemberHolder)CoreSerializer.DeserializeFromJsonObject(json, typeof(MemberHolder));

        Assert.That(restored.Width.CurrentValue, Is.EqualTo(1));
    }

    [Test]
    public void A_core_property_reads_a_former_key_from_its_builder_or_its_attribute()
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(new RenamedSettings());
        Assert.That(json.ContainsKey(nameof(RenamedSettings.Title)) && json.ContainsKey(nameof(RenamedSettings.Count)), Is.True);
        json.Remove(nameof(RenamedSettings.Title));
        json.Remove(nameof(RenamedSettings.Count));
        json["FormerTitle"] = "kept";
        json["FormerCount"] = 7;

        var restored = (RenamedSettings)CoreSerializer.DeserializeFromJsonObject(json, typeof(RenamedSettings));
        JsonObject resaved = CoreSerializer.SerializeToJsonObject(restored);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Title, Is.EqualTo("kept"));
            Assert.That(restored.Count, Is.EqualTo(7));
            Assert.That((string?)resaved[nameof(RenamedSettings.Title)], Is.EqualTo("kept"));
            Assert.That(resaved.ContainsKey("FormerTitle"), Is.False);
            Assert.That(resaved.ContainsKey("FormerCount"), Is.False);
        });
    }

    private static void RenameKey(JsonObject json, string name, string newName)
    {
        Assert.That(json.Remove(name, out JsonNode? value), Is.True, $"{name} must have been written, or the test proves nothing.");
        json[newName] = value;
    }

    [SuppressResourceClassGeneration]
    [FormerPersistedName(FormerLeafName)]
    private sealed class RenamedLeaf : EngineObject
    {
        public RenamedLeaf() => ScanProperties<RenamedLeaf>();

        public IProperty<float> Width { get; } = Property.Create(1f);
    }

    [FormerPersistedName(FormerBoxName)]
    private sealed class RenamedBox<T>;

    private sealed class CurrentHolder;

    // Gives the current name of another type as its former name.
    [FormerPersistedName("[Beutl.UnitTests].Serialization:FormerPersistedNameTests:CurrentHolder")]
    private sealed class Impostor;

    // Only used by the unload test, which forgets it for the rest of the run.
    [FormerPersistedName(FormerUnloadedName)]
    private sealed class UnloadedType;

    [FormerPersistedName("[Beutl.UnitTests]Beutl.UnitTests.Former:Outer:BadlyNamed")]
    [FormerPersistedName("Beutl.UnitTests.Former:BadlyNamed")]
    [FormerPersistedName("[Beutl.UnitTests]Beutl.UnitTests.Former:BadlyNamed<T>")]
    [FormerPersistedName("[Beutl.UnitTests]Beutl.UnitTests.Former.BadlyNamed")]
    [FormerPersistedName("")]
    private sealed class BadlyNamed;

    [SuppressResourceClassGeneration]
    private sealed class MemberHolder : EngineObject
    {
        public MemberHolder() => ScanProperties<MemberHolder>();

        [FormerPersistedName("FormerWidth")]
        public IProperty<float> Width { get; } = Property.CreateAnimatable(1f);

        [FormerPersistedName("FormerTags")]
        public IListProperty<string> Tags { get; } = Property.CreateList<string>();
    }

    private sealed class RenamedSettings : CoreObject
    {
        public static readonly CoreProperty<string?> TitleProperty;
        public static readonly CoreProperty<int> CountProperty;

        static RenamedSettings()
        {
            TitleProperty = ConfigureProperty<string?, RenamedSettings>(nameof(Title))
                .FormerPersistedName("FormerTitle")
                .Register();

            CountProperty = ConfigureProperty<int, RenamedSettings>(nameof(Count))
                .Register();
        }

        public string? Title
        {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        [FormerPersistedName("FormerCount")]
        public int Count
        {
            get => GetValue(CountProperty);
            set => SetValue(CountProperty, value);
        }
    }
}
