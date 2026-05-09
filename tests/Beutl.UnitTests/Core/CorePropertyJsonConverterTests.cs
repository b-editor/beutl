using System.Text.Json;

namespace Beutl.UnitTests.Core;

public class CorePropertyJsonConverterTests
{
    private sealed class JsonOwner : CoreObject
    {
        public static readonly CoreProperty<int> CountProperty
            = ConfigureProperty<int, JsonOwner>(nameof(Count)).Register();

        public int Count
        {
            get => GetValue(CountProperty);
            set => SetValue(CountProperty, value);
        }
    }

    private static readonly JsonSerializerOptions s_options = JsonHelper.SerializerOptions;

    [Test]
    public void Write_EmitsNameAndOwner()
    {
        // Touch the static field so the property is registered before serialization.
        _ = JsonOwner.CountProperty;
        string json = JsonSerializer.Serialize<CoreProperty>(JsonOwner.CountProperty, s_options);
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.That(doc.RootElement.GetProperty("Name").GetString(), Is.EqualTo("Count"));
        Assert.That(doc.RootElement.GetProperty("Owner").GetString(),
            Does.Contain(nameof(JsonOwner)));
    }

    [Test]
    public void Read_ResolvesPropertyByOwnerAndName()
    {
        _ = JsonOwner.CountProperty;
        string json = JsonSerializer.Serialize<CoreProperty>(JsonOwner.CountProperty, s_options);

        CoreProperty? back = JsonSerializer.Deserialize<CoreProperty>(json, s_options);
        Assert.That(back, Is.Not.Null);
        Assert.That(back!.Name, Is.EqualTo("Count"));
        Assert.That(back.OwnerType, Is.EqualTo(typeof(JsonOwner)));
    }

    [Test]
    public void Read_NonObjectJson_Throws()
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<CoreProperty>("\"not-an-object\"", s_options));
    }

    [Test]
    public void Read_MissingFields_Throws()
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<CoreProperty>("{\"Name\":\"Count\"}", s_options));
    }
}
