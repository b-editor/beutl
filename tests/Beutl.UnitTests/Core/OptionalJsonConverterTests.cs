using System.Text.Json;
using Beutl.Animation.Easings;

namespace Beutl.UnitTests.Core;

public class OptionalJsonConverterTests
{
    private static JsonSerializerOptions Options => JsonHelper.SerializerOptions;

    [Test]
    public void Serialize_EmptyOptional_WritesNothing()
    {
        // The Optional<T> JSON converter intentionally writes nothing when HasValue is false.
        // This relies on the parent serializer skipping the property entirely (Optional<T> is
        // never the root of a serialized payload), so the standalone output here is not a
        // valid JSON document by design.
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

    // An easing is not core-serializable, yet its own converter writes it as an object, which must be read
    // back through that converter.
    [Test]
    public void RoundTrip_OptionalEasing_PreservesTheEasing()
    {
        var spline = new Optional<Easing>(new SplineEasing(0.25f, 0.1f, 0.4f, 1f));
        var linear = new Optional<Easing>(new LinearEasing());

        var parsedSpline = JsonSerializer.Deserialize<Optional<Easing>>(JsonSerializer.Serialize(spline, Options), Options);
        var parsedLinear = JsonSerializer.Deserialize<Optional<Easing>>(JsonSerializer.Serialize(linear, Options), Options);

        Assert.That(parsedSpline.Value, Is.TypeOf<SplineEasing>());
        var parsed = (SplineEasing)parsedSpline.Value;
        Assert.Multiple(() =>
        {
            Assert.That((parsed.X1, parsed.Y1, parsed.X2, parsed.Y2), Is.EqualTo((0.25f, 0.1f, 0.4f, 1f)));
            Assert.That(parsedLinear.Value, Is.TypeOf<LinearEasing>());
        });
    }
}
