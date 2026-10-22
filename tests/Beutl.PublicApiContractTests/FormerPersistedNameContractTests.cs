using System.Text.Json.Nodes;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Serialization;

namespace Beutl.PublicApiContractTests;

// A plugin renames a type or a property and declares the name it was saved under, so that what an older version of
// the plugin saved still loads. Beutl writes the current names.
[TestFixture]
public sealed class FormerPersistedNameContractTests
{
    [Test]
    public void Plugin_engine_objects_load_from_their_former_type_name_and_property_key()
    {
        var source = new RenamedPluginObject();
        source.Size.Value = 7;
        JsonObject json = CoreSerializer.SerializeToJsonObject(source);
        // What the version of the plugin before the rename saved.
        json["$type"] = RenamedPluginObject.FormerTypeName;
        json[RenamedPluginObject.FormerSizeKey] = json[nameof(RenamedPluginObject.Size)]!.DeepClone();
        json.Remove(nameof(RenamedPluginObject.Size));

        object restored = CoreSerializer.DeserializeFromJsonObject(json, typeof(EngineObject));
        JsonObject resaved = CoreSerializer.SerializeToJsonObject((RenamedPluginObject)restored);

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.TypeOf<RenamedPluginObject>());
            Assert.That(((RenamedPluginObject)restored).Size.Value, Is.EqualTo(7));
            Assert.That((string?)resaved["$type"], Is.Not.EqualTo(RenamedPluginObject.FormerTypeName));
            Assert.That(resaved.ContainsKey(nameof(RenamedPluginObject.Size)), Is.True);
            Assert.That(resaved.ContainsKey(RenamedPluginObject.FormerSizeKey), Is.False);
        });
    }

    [Test]
    public void Plugin_settings_load_from_a_former_key_given_at_registration()
    {
        var settings = new RenamedPluginSettings();

        CoreSerializer.PopulateFromJsonObject(settings, new JsonObject { [RenamedPluginSettings.FormerLimitKey] = 3 });

        Assert.That(settings.Limit, Is.EqualTo(3));
    }
}

[FormerPersistedName(FormerTypeName)]
public sealed partial class RenamedPluginObject : EngineObject
{
    public const string FormerTypeName = "[Beutl.PublicApiContractTests]Beutl.PublicApiContractTests.Former:PluginObject";
    public const string FormerSizeKey = "FormerSize";

    public RenamedPluginObject() => ScanProperties<RenamedPluginObject>();

    [FormerPersistedName(FormerSizeKey)]
    public IExpressionProperty<float> Size { get; } = Property.CreateExpression(0f);
}

public sealed class RenamedPluginSettings : ExtensionSettings
{
    public const string FormerLimitKey = "FormerLimit";

    public static readonly CoreProperty<int> LimitProperty =
        ConfigureProperty<int, RenamedPluginSettings>(nameof(Limit))
            .FormerPersistedName(FormerLimitKey)
            .Register();

    public int Limit
    {
        get => GetValue(LimitProperty);
        set => SetValue(LimitProperty, value);
    }
}
