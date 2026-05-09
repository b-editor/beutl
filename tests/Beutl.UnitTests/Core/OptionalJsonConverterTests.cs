using System.Text.Json;

namespace Beutl.UnitTests.Core;

public class OptionalJsonConverterTests
{
    private static JsonSerializerOptions Options => JsonHelper.SerializerOptions;

    [Test]
    public void Serialize_EmptyOptional_WritesNothing()
    {
        Optional<int> empty = default;
        string json = JsonSerializer.Serialize(empty, Options);
        Assert.That(json, Is.Empty);
    }

    [Test]
    public void Serialize_OptionalInt_WritesNumber()
    {
        var opt = new Optional<int>(42);
        string json = JsonSerializer.Serialize(opt, Options);
        Assert.That(json, Is.EqualTo("42"));
    }

    [Test]
    public void Serialize_OptionalString_WritesQuotedString()
    {
        var opt = new Optional<string>("hello");
        string json = JsonSerializer.Serialize(opt, Options);
        Assert.That(json.Trim(), Is.EqualTo("\"hello\""));
    }

    [Test]
    public void Deserialize_Number_FromInsideOptionalProperty_ReturnsOptional()
    {
        var result = JsonSerializer.Deserialize<Optional<int>>("123", Options);
        Assert.That(result.HasValue, Is.True);
        Assert.That(result.Value, Is.EqualTo(123));
    }

    [Test]
    public void Deserialize_Number_ReturnsOptional()
    {
        var result = JsonSerializer.Deserialize<Optional<int>>("42", Options);
        Assert.That(result.HasValue, Is.True);
        Assert.That(result.Value, Is.EqualTo(42));
    }

    [Test]
    public void Deserialize_String_ReturnsOptional()
    {
        var result = JsonSerializer.Deserialize<Optional<string>>("\"hello\"", Options);
        Assert.That(result.HasValue, Is.True);
        Assert.That(result.Value, Is.EqualTo("hello"));
    }

    [Test]
    public void RoundTrip_OptionalDouble_PreservesValue()
    {
        var opt = new Optional<double>(3.14);
        string json = JsonSerializer.Serialize(opt, Options);
        var parsed = JsonSerializer.Deserialize<Optional<double>>(json, Options);
        Assert.That(parsed.HasValue, Is.True);
        Assert.That(parsed.Value, Is.EqualTo(3.14));
    }
}
