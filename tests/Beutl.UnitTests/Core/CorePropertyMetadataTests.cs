using System;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

public class CorePropertyMetadataTests
{
    private class MetaObject : CoreObject
    {
        // Captures attributes for metadata
        [NotAutoSerialized]
        public int Hidden { get; set; }

        [System.ComponentModel.DataAnnotations.Range(0, 10)]
        public int Age2
        {
            get => GetValue(Age2Property);
            set => SetValue(Age2Property, value);
        }

        [JsonConverter(typeof(IntTagConverter))]
        public int Number { get; set; }

        public static readonly CoreProperty<int> HiddenProperty;
        public static readonly CoreProperty<int> Age2Property;
        public static readonly CoreProperty<int> UnbackedProperty;
        public static readonly CoreProperty<int> NumberProperty;

        static MetaObject()
        {
            HiddenProperty = ConfigureProperty<int, MetaObject>(nameof(Hidden))
                .Accessor(o => o.Hidden, (o, v) => o.Hidden = v)
                .DefaultValue(0)
                .Register();

            Age2Property = ConfigureProperty<int, MetaObject>(nameof(Age2))
                .DefaultValue(0)
                .SetAttribute(new System.ComponentModel.DataAnnotations.RangeAttribute(0, 10))
                .Register();

            NumberProperty = ConfigureProperty<int, MetaObject>(nameof(Number))
                .Accessor(o => o.Number, (o, v) => o.Number = v)
                .DefaultValue(0)
                .Register();

            UnbackedProperty = ConfigureProperty<int, MetaObject>(nameof(Unbacked))
                .DefaultValue(0)
                .Register();
        }

        public int Unbacked
        {
            get => GetValue(UnbackedProperty);
            set => SetValue(UnbackedProperty, value);
        }
    }

    private sealed class DerivedMetaObject : MetaObject
    {
        static DerivedMetaObject()
        {
            // Override default for Number
            MetaObject.UnbackedProperty.OverrideDefaultValue<DerivedMetaObject>(5);
        }
    }

    private sealed class IntTagConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? s = reader.GetString();
            if (s != null && s.StartsWith("i:"))
            {
                return int.Parse(s.AsSpan(2));
            }
            throw new JsonException();
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        {
            writer.WriteStringValue($"i:{value}");
        }
    }

    [Test]
    public void NotAutoSerialized_ExcludedFromJson()
    {
        var obj = new MetaObject { Hidden = 123 };
        var json = new JsonObject();
        var ctx = new JsonSerializationContext(typeof(MetaObject), NullSerializationErrorNotifier.Instance, json: json);
        using (ThreadLocalSerializationContext.Enter(ctx))
        {
            obj.Serialize(ctx);
        }

        Assert.That(json.ContainsKey(nameof(MetaObject.Hidden)), Is.False);
    }

    // Note: Validation via DataAnnotations is validated in broader integration tests; per-property validators
    // can be integration-tested where they are used (e.g., editors). Keeping serialization-focused here.

    [Test]
    public void JsonConverterAttribute_OnProperty_IsUsed()
    {
        var obj = new MetaObject { Number = 42 };
        var json = new JsonObject();
        var ctx = new JsonSerializationContext(typeof(MetaObject), NullSerializationErrorNotifier.Instance, json: json);
        using (ThreadLocalSerializationContext.Enter(ctx))
        {
            obj.Serialize(ctx);
        }

        var numberNode = json[nameof(MetaObject.Number)] as JsonValue;
        Assert.That(numberNode, Is.Not.Null);
        Assert.That(numberNode!.ToJsonString(), Is.EqualTo("\"i:42\""));

        var obj2 = new MetaObject();
        var ctx2 = new JsonSerializationContext(typeof(MetaObject), NullSerializationErrorNotifier.Instance, json: json);
        using (ThreadLocalSerializationContext.Enter(ctx2))
        {
            obj2.Deserialize(ctx2);
        }
        Assert.That(obj2.Number, Is.EqualTo(42));
    }

    [Test]
    public void OverrideDefaultValue_WorksInDerivedClass()
    {
        var d = new DerivedMetaObject();
        Assert.That(d.GetValue(MetaObject.UnbackedProperty), Is.EqualTo(5));
    }
}
