using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.Animation.Easings;
using Beutl.Serialization;

namespace Beutl.UnitTests.Engine.Animation;

public class EasingSerializationTests
{
    public sealed class StatefulEasing : Easing
    {
        public float Scale { get; set; }

        public override float Ease(float progress) => progress * Scale;
    }

    public sealed class EasingHolder : CoreObject
    {
        public static readonly CoreProperty<Easing> EasingProperty =
            ConfigureProperty<Easing, EasingHolder>(nameof(Easing))
                .DefaultValue(new LinearEasing())
                .Register();

        public Easing Easing
        {
            get => GetValue(EasingProperty);
            set => SetValue(EasingProperty, value);
        }
    }

    private static IEnumerable<Type> BuiltInEasingTypes => typeof(Easing).Assembly.GetTypes()
        .Where(type => !type.IsAbstract && typeof(Easing).IsAssignableFrom(type)
                       && type.GetConstructor(Type.EmptyTypes) is not null);

    [TestCaseSource(nameof(BuiltInEasingTypes))]
    public void JsonSerializer_BaseEasingRoundTrip_PreservesConcreteType(Type type)
    {
        var source = (Easing)Activator.CreateInstance(type)!;

        string json = JsonSerializer.Serialize(source, JsonHelper.SerializerOptions);
        Easing restored = JsonSerializer.Deserialize<Easing>(json, JsonHelper.SerializerOptions)!;

        Assert.That(restored, Is.TypeOf(type));
        Assert.That(restored.Ease(0.25f), Is.EqualTo(source.Ease(0.25f)));
    }

    [Test]
    public void JsonSerializer_BaseSplineEasingRoundTrip_PreservesControlPointsAndDiscriminator()
    {
        Easing source = new SplineEasing(0.1f, -0.2f, 0.3f, 1.4f);

        JsonObject json = JsonSerializer.SerializeToNode(source, JsonHelper.SerializerOptions)!.AsObject();
        var restored = JsonSerializer.Deserialize<Easing>(json, JsonHelper.SerializerOptions) as SplineEasing;

        Assert.Multiple(() =>
        {
            Assert.That(json.GetDiscriminator(), Is.EqualTo(typeof(SplineEasing)));
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.X1, Is.EqualTo(0.1f));
            Assert.That(restored.Y1, Is.EqualTo(-0.2f));
            Assert.That(restored.X2, Is.EqualTo(0.3f));
            Assert.That(restored.Y2, Is.EqualTo(1.4f));
        });
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void JsonSerializer_SplineRoundTrip_HonorsNamingAndNumberOptions(bool camelCase, bool writeNumbersAsStrings)
    {
        var options = new JsonSerializerOptions(JsonHelper.SerializerOptions)
        {
            PropertyNamingPolicy = camelCase ? JsonNamingPolicy.CamelCase : null,
        };
        if (writeNumbersAsStrings)
            options.NumberHandling |= JsonNumberHandling.WriteAsString;
        Easing source = new SplineEasing(0.125f, -0.25f, 0.75f, 1.5f);

        string json = JsonSerializer.Serialize(source, options);
        JsonNode? x1 = JsonNode.Parse(json)![camelCase ? "x1" : "X1"];
        Assert.That(x1, Is.Not.Null);
        if (writeNumbersAsStrings)
            Assert.That(x1!.GetValue<string>(), Is.EqualTo("0.125"));
        var restored = JsonSerializer.Deserialize<Easing>(json, options) as SplineEasing;

        Assert.That(restored, Is.Not.Null);
        Assert.That(new[] { restored!.X1, restored.Y1, restored.X2, restored.Y2 },
            Is.EqualTo(new[] { 0.125f, -0.25f, 0.75f, 1.5f }));
    }

    [Test]
    public void JsonSerializer_QuotedSplinePoints_WithDefaultOptions_RoundTrips()
    {
        const string Json = """{"X1":"0.5","Y1":"-0.25","X2":"0.75","Y2":"1.5"}""";

        Easing? source = JsonSerializer.Deserialize<Easing>(Json, JsonHelper.SerializerOptions);
        string json = JsonSerializer.Serialize(source, JsonHelper.SerializerOptions);
        var restored = JsonSerializer.Deserialize<Easing>(json, JsonHelper.SerializerOptions) as SplineEasing;

        Assert.That(restored, Is.Not.Null);
        Assert.That(new[] { restored!.X1, restored.Y1, restored.X2, restored.Y2 },
            Is.EqualTo(new[] { 0.5f, -0.25f, 0.75f, 1.5f }));
    }

    [Test]
    public void JsonSerializer_QuotedSplinePoint_WithStrictNumberOptions_ThrowsJsonException()
    {
        var options = new JsonSerializerOptions(JsonHelper.SerializerOptions)
        {
            NumberHandling = JsonNumberHandling.Strict,
        };

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Easing>("""{"X1":"0.5"}""", options));
    }

    [TestCase("$type")]
    [TestCase("@type")]
    public void JsonSerializationContext_TypedEasingObject_RestoresConcreteType(string discriminatorKey)
    {
        var json = new JsonObject
        {
            ["Easing"] = new JsonObject
            {
                [discriminatorKey] = TypeFormat.ToString(typeof(HoldEasing)),
            },
        };
        var context = new JsonSerializationContext(typeof(EasingHolder), json: json);

        Easing? restored = context.GetValue<Easing>("Easing");

        Assert.That(restored, Is.TypeOf<HoldEasing>());
    }

    [TestCase(typeof(LinearEasing))]
    [TestCase(typeof(HoldEasing))]
    public void JsonSerializationContext_LegacyTypeName_RestoresConcreteType(Type type)
    {
        var context = new JsonSerializationContext(typeof(EasingHolder), json: new JsonObject
        {
            ["Easing"] = TypeFormat.ToString(type),
        });

        Assert.That(context.GetValue<Easing>("Easing"), Is.TypeOf(type));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void JsonSerializationContext_LegacySpline_UsesMissingControlPointDefaults(bool empty)
    {
        var splineJson = empty ? new JsonObject() : new JsonObject { ["X1"] = 0.1f, ["Y1"] = 0.2f };
        var context = new JsonSerializationContext(typeof(EasingHolder), json: new JsonObject
        {
            ["Easing"] = splineJson,
        });

        var restored = context.GetValue<Easing>("Easing") as SplineEasing;

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.X1, Is.EqualTo(empty ? 0 : 0.1f));
            Assert.That(restored.Y1, Is.EqualTo(empty ? 0 : 0.2f));
            Assert.That(restored.X2, Is.EqualTo(1));
            Assert.That(restored.Y2, Is.EqualTo(1));
        });
    }

    [Test]
    public void CoreSerializer_EasingPropertyRoundTrip_PreservesCustomEasingState()
    {
        var source = new EasingHolder { Easing = new StatefulEasing { Scale = 2.5f } };

        JsonObject json = CoreSerializer.SerializeToJsonObject(source);
        var restored = (EasingHolder)CoreSerializer.DeserializeFromJsonObject(json, typeof(EasingHolder));

        Assert.That(restored.Easing, Is.TypeOf<StatefulEasing>());
        Assert.That(((StatefulEasing)restored.Easing).Scale, Is.EqualTo(2.5f));
    }

    [Test]
    public void JsonSerializationContext_EasingCollectionRoundTrip_PreservesTypesAndValues()
    {
        var context = new JsonSerializationContext(typeof(EasingHolder));
        context.SetValue<Easing[]>("Easings",
        [
            new HoldEasing(),
            new SplineEasing(0.1f, 0.2f, 0.3f, 0.4f),
            new StatefulEasing { Scale = 2.5f },
        ]);

        Easing[] restored = context.GetValue<Easing[]>("Easings")!;

        Assert.Multiple(() =>
        {
            Assert.That(restored[0], Is.TypeOf<HoldEasing>());
            Assert.That(restored[1], Is.TypeOf<SplineEasing>());
            Assert.That(((SplineEasing)restored[1]).Y2, Is.EqualTo(0.4f));
            Assert.That(restored[2], Is.TypeOf<StatefulEasing>());
            Assert.That(((StatefulEasing)restored[2]).Scale, Is.EqualTo(2.5f));
        });
    }

    [TestCase("{\"$type\":\"[Missing.Assembly]Missing:Easing\"}")]
    [TestCase("{\"$type\":\"[Beutl.Engine]Beutl.Animation.Easings:Easing\"}")]
    [TestCase("{\"$type\":\"[Beutl.ProjectSystem]Beutl.ProjectSystem:Element\"}")]
    [TestCase("{\"$type\":42}")]
    [TestCase("{\"$type\":null}")]
    [TestCase("{\"$type\":42,\"@type\":\"[Beutl.Engine]Beutl.Animation.Easings:HoldEasing\"}")]
    [TestCase("{\"X1\":\"invalid\"}")]
    [TestCase("{\"X1\":-0.1}")]
    [TestCase("{\"X2\":1.1}")]
    [TestCase("{\"Y1\":\"NaN\"}")]
    [TestCase("[]")]
    [TestCase("true")]
    public void JsonSerializer_InvalidEasing_ThrowsJsonException(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Easing>(json, JsonHelper.SerializerOptions));
    }
}
